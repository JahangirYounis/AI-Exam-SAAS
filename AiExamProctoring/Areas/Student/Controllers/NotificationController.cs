
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Student.Controllers;

[Area("Student")]
[Authorize(Roles = "Student")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationController : Controller
{
    private const int RecentLimit = 8;
    private readonly ApplicationDbContext _context;

    public NotificationController(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    private string? StudentId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();
        const int size = 15;
        var query = _context.StudentNotifications.AsNoTracking()
            .Where(n => n.StudentId == studentId);
        var unread = await query.CountAsync(n => !n.IsRead, cancellationToken);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        page = Math.Clamp(page, 1, pages);
        var rows = await query.OrderByDescending(n => n.CreatedDate)
            .ThenByDescending(n => n.StudentNotificationId)
            .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        ViewBag.UnreadCount = unread;
        ViewBag.TotalCount = total;
        ViewBag.Page = page;
        ViewBag.Pages = pages;
        ViewBag.UnreadOnly = unreadOnly;
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Recent(bool history = false, int page = 0)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Unauthorized();
        var ct = HttpContext.RequestAborted;

        var query = _context.StudentNotifications.AsNoTracking()
            .Where(notification => notification.StudentId == studentId);
        var unreadCount = await query.CountAsync(notification => !notification.IsRead, ct);
        page = Math.Clamp(page, 0, 100000);
        var rows = await query.Where(n => history || !n.IsRead)
            .OrderByDescending(notification => notification.CreatedDate)
            .ThenByDescending(notification => notification.StudentNotificationId)
            .Skip(page * RecentLimit)
            .Take(RecentLimit)
            .Select(notification => new
            {
                id = notification.StudentNotificationId,
                notification.Title,
                notification.Message,
                notification.IsRead,
                notification.Type,
                createdAt = notification.CreatedDate,
                examId = notification.ExamId
            })
            .ToListAsync(ct);

        return Json(new { unread = unreadCount, items = rows.Select(n => new { n.id, n.Title, n.Message, n.IsRead, n.Type, n.createdAt, n.examId, url = Url.Action("Open", "Notification", new { area = "Student", id = n.id }) }) });
    }

    [HttpGet]
    public async Task<IActionResult> Open(int id)
    {
        var item = await _context.StudentNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.StudentNotificationId == id && n.StudentId == StudentId);
        if (item is null) return NotFound();
        // Reading a specific notification acknowledges only that item, never the whole inbox.
        if (!item.IsRead)
        {
            var row = await _context.StudentNotifications.SingleAsync(n =>
                n.StudentNotificationId == id && n.StudentId == StudentId);
            row.IsRead = true;
            row.ReadDate = DateTime.Now;
            await _context.SaveChangesAsync();
        }
        // An exam publication (ExamReleased) is a NEW available exam, not a result.
        // Admission result publications should open the student's exact released result when available.
        if (item.Type.StartsWith("ResultPublished:", StringComparison.Ordinal) &&
            int.TryParse(item.Type["ResultPublished:".Length..], out var publishedAttemptId))
        {
            var exactId = await Services.Results.StudentResultAccess.ReleasedFor(_context, StudentId!)
                .Where(r => r.AttemptId == publishedAttemptId).Select(r => (int?)r.ResultId).SingleOrDefaultAsync();
            return exactId.HasValue ? RedirectToAction("Details", "Result", new { area = "Student", id = exactId.Value })
                : RedirectToAction("Index", "Result", new { area = "Student" });
        }
        if (item.Type == "ResultPublished")
        {
            var resultId = await Services.Results.StudentResultAccess.ReleasedFor(_context, StudentId!)
                .Where(r => r.Attempt != null && r.Attempt.ExamId == item.ExamId)
                .OrderByDescending(r => r.ResultId)
                .Select(r => (int?)r.ResultId)
                .FirstOrDefaultAsync();
            return resultId.HasValue
                ? RedirectToAction("Details", "Result", new { area = "Student", id = resultId.Value })
                : RedirectToAction("Index", "Result", new { area = "Student" });
        }
        if (item.ExamId is int examId)
        {
            var attempt = await _context.ExamAttempts.AsNoTracking().Where(a => a.StudentId == StudentId && a.ExamId == examId).OrderByDescending(a => a.AttemptId).FirstOrDefaultAsync();
            if (attempt is not null) return RedirectToAction(attempt.Status == AttemptStatus.ProctoringBlocked ? "Blocked" : attempt.Status == AttemptStatus.InProgress ? "Resume" : "AttemptResult", "Exam", new { area = "Student", attemptId = attempt.AttemptId });
            return RedirectToAction("PreCheck", "Exam", new { area = "Student", examId });
        }
        return RedirectToAction("Index", "Dashboard", new { area = "Student" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromForm] int[]? ids)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Unauthorized();
        if (!ModelState.IsValid || ids is null || ids.Length > RecentLimit || ids.Any(id => id <= 0))
            return BadRequest();
        if (ids.Length == 0) return Json(new { ok = true });

        var requestedIds = ids.Distinct().ToArray();
        var query = _context.StudentNotifications.Where(notification =>
            notification.StudentId == studentId && !notification.IsRead &&
            requestedIds.Contains(notification.StudentNotificationId));
        await MarkReadAsync(query, DateTime.Now, HttpContext.RequestAborted);
        return Json(new { ok = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Unauthorized();
        var query = _context.StudentNotifications.Where(notification =>
            notification.StudentId == studentId && !notification.IsRead);
        await MarkReadAsync(query, DateTime.Now, HttpContext.RequestAborted);
        return Json(new { ok = true });
    }

    private async Task MarkReadAsync(IQueryable<StudentNotification> query, DateTime readAt, CancellationToken ct)
    {
        if (_context.Database.IsRelational())
        {
            // One UPDATE on SQL Server/Azure SQL; do not load an entire inbox.
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.IsRead, true)
                .SetProperty(notification => notification.ReadDate, (DateTime?)readAt), ct);
            return;
        }

        // Non-relational EF providers do not implement ExecuteUpdate.
        var notifications = await query.ToListAsync(ct);
        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadDate = readAt;
        }
        if (notifications.Count > 0) await _context.SaveChangesAsync(ct);
    }
}

