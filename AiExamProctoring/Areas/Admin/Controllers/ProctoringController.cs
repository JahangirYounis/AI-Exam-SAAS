using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers;

/// <summary>
/// Platform-wide, read-only integrity governance. The Teacher who owns an exam
/// reviews warnings and decides whether an attempt should be resumed.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
public sealed class ProctoringController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IProctoringEvidenceStore _evidence;
    private readonly IProctoringStateService _states;

    public ProctoringController(ApplicationDbContext db, IProctoringEvidenceStore evidence, IProctoringStateService states)
    {
        _db = db;
        _evidence = evidence;
        _states = states;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 24;
        page = Math.Max(1, page);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var query = _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.Student != null && x.ExamId != null && x.Exam != null);

        if (search is not null)
        {
            query = query.Where(x =>
                x.Student!.FullName.Contains(search) ||
                (x.Student.CandidateNumber != null && x.Student.CandidateNumber.Contains(search)) ||
                (x.Student.Email != null && x.Student.Email.Contains(search)) ||
                x.Exam!.ExamTitle.Contains(search));
        }

        var totalCandidates = await query.Select(x => x.StudentId)
            .Distinct().CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCandidates / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await query
            .GroupBy(x => new
            {
                x.StudentId,
                x.Student!.FullName,
                x.Student.CandidateNumber,
                x.Student.Email,
                x.Student.ProfileImage
            })
            .Select(g => new
            {
                g.Key.StudentId,
                CandidateName = g.Key.FullName,
                g.Key.CandidateNumber,
                g.Key.Email,
                g.Key.ProfileImage,
                ExamFolderCount = g.Select(x => x.ExamId).Distinct().Count(),
                AlertCount = g.Count(),
                ViolationCount = g.Count(x => x.CountsAsViolation),
                PendingCount = g.Count(x => !x.IsReviewed),
                EvidenceCount = g.Count(x => x.ScreenshotPath != null || x.RecordingPath != null),
                HighSeverityCount = g.Count(x => x.Severity == ProctoringSeverity.High),
                LatestEventAt = g.Max(x => x.CreatedDate)
            })
            .OrderByDescending(x => x.PendingCount)
            .ThenByDescending(x => x.LatestEventAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var folders = rows.Select(x => new CandidateProctoringFolderViewModel
        {
            StudentId = x.StudentId,
            CandidateName = string.IsNullOrWhiteSpace(x.CandidateName) ? "Unknown candidate" : x.CandidateName,
            CandidateNumber = string.IsNullOrWhiteSpace(x.CandidateNumber) ? "Not assigned" : x.CandidateNumber,
            Email = x.Email ?? string.Empty,
            ProfileImage = x.ProfileImage,
            ExamFolderCount = x.ExamFolderCount,
            AlertCount = x.AlertCount,
            ViolationCount = x.ViolationCount,
            PendingCount = x.PendingCount,
            EvidenceCount = x.EvidenceCount,
            HighSeverityCount = x.HighSeverityCount,
            LatestEventAt = x.LatestEventAt
        }).ToList();

        ViewBag.Search = search;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalCandidates = totalCandidates;
        ViewBag.TotalEvents = await _db.ProctoringLogs.AsNoTracking().CountAsync(cancellationToken);
        ViewBag.PendingEvents = await _db.ProctoringLogs.AsNoTracking().CountAsync(x => !x.IsReviewed, cancellationToken);
        ViewBag.EvidenceEvents = await _db.ProctoringLogs.AsNoTracking().CountAsync(x => x.ScreenshotPath != null || x.RecordingPath != null, cancellationToken);
        return View(folders);
    }

    [HttpGet]
    public async Task<IActionResult> Candidate(string studentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return NotFound();

        var student = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == studentId, cancellationToken);
        if (student is null) return NotFound();

        var rows = await _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.ExamId != null && x.Exam != null)
            .GroupBy(x => new
            {
                ExamId = x.ExamId!.Value,
                x.Exam!.ExamTitle,
                x.Exam.CreatedBy,
                x.Exam.Audience,
                x.Exam.Purpose
            })
            .Select(g => new
            {
                g.Key.ExamId,
                g.Key.ExamTitle,
                g.Key.CreatedBy,
                g.Key.Audience,
                g.Key.Purpose,
                AttemptCount = g.Where(x => x.AttemptId != null).Select(x => x.AttemptId).Distinct().Count(),
                AlertCount = g.Count(),
                ViolationCount = g.Count(x => x.CountsAsViolation),
                PendingCount = g.Count(x => !x.IsReviewed),
                EvidenceCount = g.Count(x => x.ScreenshotPath != null || x.RecordingPath != null),
                HighSeverityCount = g.Count(x => x.Severity == ProctoringSeverity.High),
                LatestEventAt = g.Max(x => x.CreatedDate)
            })
            .OrderByDescending(x => x.LatestEventAt)
            .ToListAsync(cancellationToken);

        var teacherIds = rows.Where(x => !string.IsNullOrWhiteSpace(x.CreatedBy))
            .Select(x => x.CreatedBy!).Distinct().ToList();
        var teacherNames = await _db.Users.AsNoTracking()
            .Where(x => teacherIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var examFolders = rows.Select(x => new ExamProctoringFolderViewModel
        {
            ExamId = x.ExamId,
            ExamTitle = x.ExamTitle,
            TeacherName = x.CreatedBy is not null && teacherNames.TryGetValue(x.CreatedBy, out var name)
                ? name
                : "Unknown / legacy teacher",
            Audience = x.Audience,
            Purpose = x.Purpose,
            AttemptCount = x.AttemptCount,
            AlertCount = x.AlertCount,
            ViolationCount = x.ViolationCount,
            PendingCount = x.PendingCount,
            EvidenceCount = x.EvidenceCount,
            HighSeverityCount = x.HighSeverityCount,
            LatestEventAt = x.LatestEventAt
        }).ToList();

        return View(new ProctoringCandidateFolderPageViewModel
        {
            Candidate = BuildCandidateFolder(student, examFolders),
            ExamFolders = examFolders,
            TeacherScope = false
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExamFolder(
        string studentId,
        int examId,
        bool pendingOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentId) || examId <= 0) return NotFound();

        var student = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == studentId, cancellationToken);
        var exam = await _db.Exams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExamId == examId, cancellationToken);
        if (student is null || exam is null) return NotFound();

        var query = _db.ProctoringLogs.AsNoTracking()
            .Include(x => x.Student)
            .Include(x => x.Exam)
            .Include(x => x.Attempt)
            .Include(x => x.SessionAttempt).ThenInclude(x => x!.Session)
            .Where(x => x.StudentId == studentId && x.ExamId == examId);
        if (pendingOnly) query = query.Where(x => !x.IsReviewed);

        var logs = await query.OrderByDescending(x => x.CreatedDate)
            .ThenByDescending(x => x.LogId)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var allSummary = await _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.ExamId == examId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                AlertCount = g.Count(),
                AttemptCount = g.Where(x => x.AttemptId != null).Select(x => x.AttemptId).Distinct().Count(),
                ViolationCount = g.Count(x => x.CountsAsViolation),
                PendingCount = g.Count(x => !x.IsReviewed),
                EvidenceCount = g.Count(x => x.ScreenshotPath != null || x.RecordingPath != null),
                HighSeverityCount = g.Count(x => x.Severity == ProctoringSeverity.High),
                LatestEventAt = g.Max(x => x.CreatedDate)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var teacherName = string.IsNullOrWhiteSpace(exam.CreatedBy)
            ? "Unknown / legacy teacher"
            : await _db.Users.AsNoTracking().Where(x => x.Id == exam.CreatedBy)
                .Select(x => x.FullName).FirstOrDefaultAsync(cancellationToken) ?? "Unknown teacher";
        var examFolder = new ExamProctoringFolderViewModel
        {
            ExamId = exam.ExamId,
            ExamTitle = exam.ExamTitle,
            TeacherName = teacherName,
            Audience = exam.Audience,
            Purpose = exam.Purpose,
            AttemptCount = allSummary?.AttemptCount ?? 0,
            AlertCount = allSummary?.AlertCount ?? 0,
            ViolationCount = allSummary?.ViolationCount ?? 0,
            PendingCount = allSummary?.PendingCount ?? 0,
            EvidenceCount = allSummary?.EvidenceCount ?? 0,
            HighSeverityCount = allSummary?.HighSeverityCount ?? 0,
            LatestEventAt = allSummary?.LatestEventAt
        };

        return View(new ProctoringExamFolderPageViewModel
        {
            BlockedAttemptIds = await _db.ExamAttempts.AsNoTracking().Where(a => a.ExamId == examId && a.StudentId == studentId && a.Status == AttemptStatus.ProctoringBlocked).OrderBy(a => a.AttemptId).Select(a => a.AttemptId).ToListAsync(cancellationToken),
            Candidate = BuildCandidateFolder(student, new[] { examFolder }),
            ExamFolder = examFolder,
            Logs = logs,
            TeacherScope = false,
            PendingOnly = pendingOnly
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var log = await _db.ProctoringLogs.AsNoTracking()
            .Include(x => x.Student)
            .Include(x => x.Exam)
            .Include(x => x.Attempt)
            .Include(x => x.SessionAttempt).ThenInclude(x => x!.Session)
            .FirstOrDefaultAsync(x => x.LogId == id, cancellationToken);
        return log is null ? NotFound() : View(log);
    }

    [HttpGet]
    public async Task<IActionResult> Evidence(int id, string? kind, CancellationToken cancellationToken)
    {
        var paths = await _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.LogId == id)
            .Select(x => new { x.ScreenshotPath, x.RecordingPath })
            .FirstOrDefaultAsync(cancellationToken);
        var storageKey = string.Equals(kind, "recording", StringComparison.OrdinalIgnoreCase)
            ? paths?.RecordingPath ?? (paths?.ScreenshotPath?.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) == true ? paths.ScreenshotPath : null)
            : paths?.ScreenshotPath;
        var file = await _evidence.OpenReadAsync(storageKey, cancellationToken);
        if (file is null) return NotFound();

        Response.Headers.CacheControl = "private,no-store,max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnblockAttempt(int attemptId, string studentId, int examId, string reason, CancellationToken cancellationToken)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actorId is null) return Challenge();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300) return BadRequest("Provide a review reason in 10–300 characters.");
        if (!await _db.ExamAttempts.AnyAsync(a => a.AttemptId == attemptId && a.StudentId == studentId && a.ExamId == examId, cancellationToken)) return NotFound();
        var state = await _states.ControlAsync(attemptId, actorId, true, reason.Trim(), cancellationToken);
        if (state is null) return Conflict("Attempt unavailable or not blocked.");
        TempData["Success"] = "Examination resumed with saved answers and frozen time restored.";
        return RedirectToAction(nameof(ExamFolder), new { studentId, examId });
    }

    private static CandidateProctoringFolderViewModel BuildCandidateFolder(
        ApplicationUser student,
        IEnumerable<ExamProctoringFolderViewModel> exams)
    {
        var folders = exams.ToList();
        return new CandidateProctoringFolderViewModel
        {
            StudentId = student.Id,
            CandidateName = student.FullName,
            CandidateNumber = student.CandidateNumber ?? "Not assigned",
            Email = student.Email ?? string.Empty,
            ProfileImage = student.ProfileImage,
            ExamFolderCount = folders.Count,
            AlertCount = folders.Sum(x => x.AlertCount),
            ViolationCount = folders.Sum(x => x.ViolationCount),
            PendingCount = folders.Sum(x => x.PendingCount),
            EvidenceCount = folders.Sum(x => x.EvidenceCount),
            HighSeverityCount = folders.Sum(x => x.HighSeverityCount),
            LatestEventAt = folders.Count == 0 ? null : folders.Max(x => x.LatestEventAt)
        };
    }
}
