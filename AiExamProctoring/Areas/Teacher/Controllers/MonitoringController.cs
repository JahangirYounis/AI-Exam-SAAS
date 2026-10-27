using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MonitoringController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IProctoringStateService _states;
    private readonly IProctoringEvidenceStore _evidence;

    public MonitoringController(ApplicationDbContext db, IProctoringEvidenceStore evidence, IProctoringStateService states)
    {
        _db = db;
        _states = states;
        _evidence = evidence;
    }

    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Live(int? examId)
    {
        var exams = await _db.Exams.AsNoTracking().Where(e => (e.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == e.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)))
            .OrderByDescending(e => e.ExamId).Select(e => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = e.ExamId.ToString(),
                Text = e.ExamTitle
            }).ToListAsync(HttpContext.RequestAborted);
        if (examId.HasValue && !exams.Any(e => e.Value == examId.ToString())) return NotFound();
        ViewBag.Exams = exams;
        ViewBag.ExamId = examId ?? (exams.Count > 0 ? int.Parse(exams[0].Value) : 0);
        return View();
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LiveRoster(int examId, int page = 1)
    {
        if (!await _db.Exams.AnyAsync(e => e.ExamId == examId && (e.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == e.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)))) return NotFound();
        var attendance = await _db.ExamInvigilators.SingleOrDefaultAsync(i => i.ExamId == examId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null);
        if (attendance is not null && (!attendance.LastMonitoringAtUtc.HasValue || attendance.LastMonitoringAtUtc < DateTime.UtcNow.AddMinutes(-1)))
        {
            attendance.FirstMonitoringAtUtc ??= DateTime.UtcNow;
            attendance.LastMonitoringAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        var owned = await _db.Exams.SingleOrDefaultAsync(e => e.ExamId == examId && e.CreatedBy == TeacherId);
        if (owned is not null && (!owned.CreatorLastMonitoringAtUtc.HasValue || owned.CreatorLastMonitoringAtUtc < DateTime.UtcNow.AddMinutes(-1)))
        {
            owned.CreatorFirstMonitoringAtUtc ??= DateTime.UtcNow;
            owned.CreatorLastMonitoringAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        var query = _db.ExamAttempts.AsNoTracking().Where(a => a.ExamId == examId &&
            (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked));
        var count = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (count + 11) / 12));
        var alertKinds = new[] { ProctoringAlertType.ReviewRequested, ProctoringAlertType.IdentityMismatch, ProctoringAlertType.FaceNotDetected,
            ProctoringAlertType.MultipleFaces, ProctoringAlertType.HeadAway, ProctoringAlertType.GazeAway,
            ProctoringAlertType.LookAway, ProctoringAlertType.EyesClosed, ProctoringAlertType.MouthMovement,
            ProctoringAlertType.AudioDetected, ProctoringAlertType.TalkingDetected, ProctoringAlertType.AnswerPromptSpeech,
            ProctoringAlertType.ElectronicDeviceDetected, ProctoringAlertType.CameraAccessLost,
            ProctoringAlertType.MicrophoneAccessLost, ProctoringAlertType.TabSwitch, ProctoringAlertType.FullScreenExit };
        var rows = await query.OrderByDescending(a => a.Status == AttemptStatus.ProctoringBlocked).ThenByDescending(a => a.LastViolationAtUtc).ThenBy(a => a.AttemptId).Skip((page - 1) * 12).Take(12)
            .Select(a => new {
                a.AttemptId,
                a.StudentId,
                a.StrikeCount,
                blockingMode = a.Exam!.BlockingMode.ToString(),
                version = a.StateVersion,
                name = a.Student!.FullName,
                status = a.Status.ToString(),
                alert = _db.ProctoringLogs.Where(l => l.AttemptId == a.AttemptId && !l.IsReviewed &&
                    alertKinds.Contains(l.AlertType))
                    .OrderByDescending(l => l.AlertType == ProctoringAlertType.ReviewRequested)
                    .ThenByDescending(l => l.LogId).Select(l => new { l.LogId, kind = l.AlertType.ToString(), l.Description }).FirstOrDefault()
            }).ToListAsync(HttpContext.RequestAborted);
        return Json(new { rows, page, pages = Math.Max(1, (count + 11) / 12), count });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PauseLive(int attemptId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300)
            return BadRequest("Describe what you reviewed in 10–300 characters.");
        var state = await _states.ControlAsync(attemptId, TeacherId!, false, reason.Trim(), HttpContext.RequestAborted);
        return state is null ? Conflict("Attempt unavailable or no longer active.") : Json(state);
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (TeacherId is null) return Challenge();
        const int pageSize = 24;
        page = Math.Max(1, page);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var query = _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.Student != null && x.Exam != null && (x.Exam.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)));
        if (search is not null)
        {
            query = query.Where(x =>
                x.Student!.FullName.Contains(search) ||
                (x.Student.CandidateNumber != null && x.Student.CandidateNumber.Contains(search)) ||
                x.Exam!.ExamTitle.Contains(search));
        }

        var totalCandidates = await query.Select(x => x.StudentId).Distinct().CountAsync(cancellationToken);
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
            CandidateName = x.CandidateName,
            CandidateNumber = x.CandidateNumber ?? "Not assigned",
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
        ViewBag.TotalEvents = await query.CountAsync(cancellationToken);
        ViewBag.PendingEvents = await query.CountAsync(x => !x.IsReviewed, cancellationToken);
        ViewBag.BlockedAttempts = await _db.ExamAttempts.AsNoTracking()
            .CountAsync(x => x.Status == AttemptStatus.ProctoringBlocked && x.Exam != null && (x.Exam.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)), cancellationToken);
        return View(folders);
    }

    [HttpGet]
    public async Task<IActionResult> Candidate(string studentId, CancellationToken cancellationToken = default)
    {
        if (TeacherId is null) return Challenge();
        if (string.IsNullOrWhiteSpace(studentId)) return NotFound();
        var student = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == studentId, cancellationToken);
        if (student is null) return NotFound();

        var rows = await _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.Exam != null && (x.Exam.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)))
            .GroupBy(x => new { ExamId = x.ExamId!.Value, x.Exam!.ExamTitle, x.Exam.Audience, x.Exam.Purpose })
            .Select(g => new
            {
                g.Key.ExamId,
                g.Key.ExamTitle,
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
        if (rows.Count == 0) return NotFound();

        var examFolders = rows.Select(x => new ExamProctoringFolderViewModel
        {
            ExamId = x.ExamId,
            ExamTitle = x.ExamTitle,
            TeacherName = User.Identity?.Name ?? "Teacher",
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
            TeacherScope = true
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExamFolder(
        string studentId,
        int examId,
        bool pendingOnly = false,
        bool recordingsOnly = false,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (TeacherId is null) return Challenge();
        var exam = await _db.Exams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExamId == examId && (x.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)), cancellationToken);
        var student = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == studentId, cancellationToken);
        if (exam is null || student is null) return NotFound();

        var allQuery = _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.ExamId == examId && (x.Exam!.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)));
        IQueryable<ProctoringLog> query = allQuery
            .Include(x => x.Student)
            .Include(x => x.Exam)
            .Include(x => x.Attempt)
            .Include(x => x.SessionAttempt).ThenInclude(x => x!.Session);
        if (pendingOnly) query = query.Where(x => !x.IsReviewed);
        query = query.Where(x => recordingsOnly
            ? x.AlertType == ProctoringAlertType.SessionRecording
            : x.AlertType != ProctoringAlertType.SessionRecording);
        const int pageSize = 50;
        var pages = Math.Max(1, (int)Math.Ceiling(await query.CountAsync(cancellationToken) / (double)pageSize));
        page = Math.Clamp(page, 1, pages);
        ViewBag.RecordingPage = page; ViewBag.RecordingPages = pages; ViewBag.RecordingsOnly = recordingsOnly;

        var logs = await query.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.LogId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var summary = await allQuery.GroupBy(_ => 1).Select(g => new
        {
            AlertCount = g.Count(),
            AttemptCount = g.Where(x => x.AttemptId != null).Select(x => x.AttemptId).Distinct().Count(),
            ViolationCount = g.Count(x => x.CountsAsViolation),
            PendingCount = g.Count(x => !x.IsReviewed),
            EvidenceCount = g.Count(x => x.ScreenshotPath != null || x.RecordingPath != null),
            HighSeverityCount = g.Count(x => x.Severity == ProctoringSeverity.High),
            LatestEventAt = g.Max(x => x.CreatedDate)
        }).FirstOrDefaultAsync(cancellationToken);
        if (summary is null) return NotFound();

        var folder = new ExamProctoringFolderViewModel
        {
            ExamId = exam.ExamId,
            ExamTitle = exam.ExamTitle,
            TeacherName = User.Identity?.Name ?? "Teacher",
            Audience = exam.Audience,
            Purpose = exam.Purpose,
            AttemptCount = summary.AttemptCount,
            AlertCount = summary.AlertCount,
            ViolationCount = summary.ViolationCount,
            PendingCount = summary.PendingCount,
            EvidenceCount = summary.EvidenceCount,
            HighSeverityCount = summary.HighSeverityCount,
            LatestEventAt = summary.LatestEventAt
        };
        return View(new ProctoringExamFolderPageViewModel
        {
            BlockedAttemptIds = await _db.ExamAttempts.AsNoTracking().Where(a => a.ExamId == examId && a.StudentId == studentId && a.Status == AttemptStatus.ProctoringBlocked).OrderBy(a => a.AttemptId).Select(a => a.AttemptId).ToListAsync(cancellationToken),
            Candidate = BuildCandidateFolder(student, new[] { folder }),
            ExamFolder = folder,
            Logs = logs,
            TeacherScope = true,
            PendingOnly = pendingOnly
        });
    }

    [HttpGet]
    public async Task<IActionResult> Evidence(int id, string? kind, CancellationToken cancellationToken)
    {
        if (TeacherId is null) return Challenge();
        var paths = await _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.LogId == id && x.Exam != null && (x.Exam.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)))
            .Select(x => new { x.ScreenshotPath, x.RecordingPath, x.VideoEvidencePath }).FirstOrDefaultAsync(cancellationToken);
        var key = string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase) ? paths?.VideoEvidencePath : string.Equals(kind, "recording", StringComparison.OrdinalIgnoreCase)
            ? paths?.RecordingPath ?? (paths?.ScreenshotPath?.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) == true ? paths.ScreenshotPath : null)
            : paths?.ScreenshotPath;
        var file = await _evidence.OpenReadAsync(key, cancellationToken);
        if (file is null) return NotFound();
        Response.Headers.CacheControl = "private,no-store,max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReviewed(int id, string studentId, int examId, CancellationToken cancellationToken)
    {
        if (TeacherId is null) return Challenge();
        var log = await _db.ProctoringLogs.FirstOrDefaultAsync(
            x => x.LogId == id && x.StudentId == studentId && x.ExamId == examId && (x.Exam!.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)),
            cancellationToken);
        if (log is null) return NotFound();
        log.IsReviewed = true;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Integrity event marked as reviewed.";
        return RedirectToAction(nameof(ExamFolder), new { studentId, examId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkExamReviewed(string studentId, int examId, CancellationToken cancellationToken)
    {
        if (TeacherId is null) return Challenge();
        var logs = await _db.ProctoringLogs.Where(x =>
                x.StudentId == studentId && x.ExamId == examId && !x.IsReviewed && (x.Exam!.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == x.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)))
            .ToListAsync(cancellationToken);
        if (logs.Count == 0) return RedirectToAction(nameof(ExamFolder), new { studentId, examId });
        foreach (var log in logs) log.IsReviewed = true;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"{logs.Count} event(s) marked as reviewed.";
        return RedirectToAction(nameof(ExamFolder), new { studentId, examId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnblockAttempt(int attemptId, string studentId, int examId, string reason, CancellationToken cancellationToken)
    {
        if (TeacherId is null) return Challenge();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300) return BadRequest("Provide a review reason in 10–300 characters.");
        if (!await _db.ExamAttempts.AnyAsync(a => a.AttemptId == attemptId && a.StudentId == studentId && a.ExamId == examId, cancellationToken)) return NotFound();
        var state = await _states.ControlAsync(attemptId, TeacherId, true, reason.Trim(), cancellationToken);
        if (state is null) return Conflict("Attempt unavailable or not blocked.");
        TempData["Success"] = "Examination resumed with saved answers and frozen time restored.";
        return RedirectToAction(nameof(ExamFolder), new { studentId, examId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PauseAfterReview(int logId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300) return BadRequest("Provide a review reason in 10–300 characters.");
        var evidence = await _db.ProctoringLogs.AsNoTracking().FirstOrDefaultAsync(l => l.LogId == logId && (l.Exam!.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == l.ExamId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)), cancellationToken);
        if (evidence?.AttemptId is null) return NotFound();
        var state = await _states.ControlAsync(evidence.AttemptId.Value, TeacherId!, false, reason.Trim(), cancellationToken);
        if (state is null) return Conflict("Attempt unavailable or no longer active.");
        return RedirectToAction(nameof(ExamFolder), new { studentId = evidence.StudentId, examId = evidence.ExamId });
    }

    private static CandidateProctoringFolderViewModel BuildCandidateFolder(ApplicationUser student, IEnumerable<ExamProctoringFolderViewModel> exams)
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
