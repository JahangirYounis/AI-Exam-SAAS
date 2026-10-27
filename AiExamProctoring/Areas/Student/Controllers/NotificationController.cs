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
    public async Task<IActionResult> Recent()
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Unauthorized();
        var ct = HttpContext.RequestAborted;

        var query = _context.StudentNotifications.AsNoTracking()
            .Where(notification => notification.StudentId == studentId);
        var unreadCount = await query.CountAsync(notification => !notification.IsRead, ct);
        var items = await query
            .OrderByDescending(notification => notification.CreatedDate)
            .ThenByDescending(notification => notification.StudentNotificationId)
            .Take(RecentLimit)
            .Select(notification => new
            {
                id = notification.StudentNotificationId,
                notification.Title,
                notification.Message,
                notification.IsRead,
                createdAt = notification.CreatedDate,
                examId = notification.ExamId
            })
            .ToListAsync(ct);

        return Json(new { unread = unreadCount, items });
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
