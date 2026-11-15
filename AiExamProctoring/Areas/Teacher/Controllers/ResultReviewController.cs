using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher"), Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResultReviewController(ApplicationDbContext db) : Controller
{
    string TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    [HttpGet]
    public async Task<IActionResult> Index(int examId, string status = "pending", string? search = null,
        int page = 1, CancellationToken ct = default)
    {
        if (!await db.Exams.AnyAsync(e => e.ExamId == examId && e.CreatedBy == TeacherId, ct))
            return NotFound();
        status = status?.Trim().ToLowerInvariant() switch
        {
            "released" => "released", "all" => "all", _ => "pending"
        };
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 100) search = search[..100];
        var scoped = db.ExamAttempts.AsNoTracking().Where(a => a.ExamId == examId);
        ViewBag.Total = await scoped.CountAsync(ct);
        ViewBag.Released = await scoped.CountAsync(a => a.ResultReleasedAtUtc != null, ct);
        ViewBag.Pending = await scoped.CountAsync(a => a.ResultReleasedAtUtc == null &&
            (a.Status == AttemptStatus.Completed || a.Status == AttemptStatus.AutoSubmitted ||
             a.Status == AttemptStatus.ProctoringBlocked || a.Status == AttemptStatus.Disqualified), ct);
        ViewBag.Blocked = await scoped.CountAsync(a => a.Status == AttemptStatus.ProctoringBlocked, ct);
        var filtered = status switch
        {
            "released" => scoped.Where(a => a.ResultReleasedAtUtc != null),
            "pending" => scoped.Where(a => a.ResultReleasedAtUtc == null &&
                (a.Status == AttemptStatus.Completed || a.Status == AttemptStatus.AutoSubmitted ||
                 a.Status == AttemptStatus.ProctoringBlocked || a.Status == AttemptStatus.Disqualified)),
            _ => scoped
        };
        if (search is not null)
            filtered = filtered.Where(a => a.Student != null &&
                (a.Student.FullName.Contains(search) ||
                 (a.Student.CandidateNumber != null && a.Student.CandidateNumber.Contains(search))));
        const int pageSize = 25;
        var count = await filtered.CountAsync(ct);
        var pages = Math.Max(1, (count + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pages);
        ViewBag.ExamId = examId;
        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.Count = count;
        ViewBag.Page = page;
        ViewBag.Pages = pages;
        ViewBag.ExamTitle = await db.Exams.Where(e => e.ExamId == examId)
            .Select(e => e.ExamTitle).FirstOrDefaultAsync(ct);
        ViewBag.Audits = await db.ProctoringLogs.AsNoTracking()
            .Where(l => l.ExamId == examId && l.ModelName == ExamResultFinalizationService.AuditModel)
            .OrderByDescending(l => l.LogId).Take(100).ToListAsync(ct);
        return View(await filtered.Include(a => a.Student).Include(a => a.Result)
            .OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.AttemptId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct));
    }
    [HttpGet]
    public async Task<IActionResult> Preview(int attemptId, CancellationToken ct = default)
    {
        var attempt = await db.ExamAttempts.AsNoTracking().Include(a => a.Exam).Include(a => a.Student)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.Exam!.CreatedBy == TeacherId, ct);
        if (attempt == null) return NotFound();
        if (attempt.Status is AttemptStatus.InProgress or AttemptStatus.NotStarted) return Conflict("This attempt is still active.");
        var answers = await db.StudentAnswers.AsNoTracking().Include(a => a.Question)
            .Where(a => a.SessionAttempt!.AttemptId == attemptId).OrderBy(a => a.SessionAttemptId).ThenBy(a => a.QuestionId).ToListAsync(ct);
        ViewBag.AnswerRows = answers.Select(a => AiExamProctoring.ViewModels.Student.ResultAnswerRow.From(a.Question, a, a.SessionAttemptId, attempt.Exam!.ShuffleOptions)).ToList();
        return View(attempt);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int attemptId, string decision, string reason, CancellationToken ct = default)
    {
        var examId = await db.ExamAttempts.Where(a => a.AttemptId == attemptId && a.Exam!.CreatedBy == TeacherId).Select(a => (int?)a.ExamId).SingleOrDefaultAsync(ct);
        if (examId == null) return NotFound();
        try { TempData["Success"] = await ExamResultFinalizationService.ReviewAsync(db, attemptId, TeacherId, decision, reason, ct); }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        return RedirectToAction(nameof(Index), new { examId });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveAll(int examId, string reason, CancellationToken ct = default)
    {
        try
        {
            var count = await ExamResultFinalizationService.PublishEligibleAsync(db, examId, TeacherId, reason, ct);
            TempData["Success"] = $"Published {count} eligible results. Active, blocked and unresolved results were not approved.";
        }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        return RedirectToAction(nameof(Index), new { examId });
    }
}
