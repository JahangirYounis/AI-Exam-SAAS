using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Pdf;
using AiExamProctoring.Services.Results;
using AiExamProctoring.ViewModels.Student;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;

namespace AiExamProctoring.Areas.Student.Controllers;

[Area("Student")]
[Authorize(Roles="Student")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ResultController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ResultPdfService _pdf;
    private readonly CertificatePdfService _certificatePdf;
    private readonly IWebHostEnvironment _environment;

    public ResultController(
        ApplicationDbContext context,
        ResultPdfService pdf,
        CertificatePdfService certificatePdf,
        IWebHostEnvironment environment)
    {
        _context = context;
        _pdf = pdf;
        _certificatePdf = certificatePdf;
        _environment = environment;
    }

    private string? StudentId=>User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, int page = 1, int pendingPage = 1)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();

        const int pageSize = 12;
        const int pendingPageSize = 6;
        var releasedQuery = StudentResultAccess.ReleasedFor(_context, studentId);
        var releasedTotal = await releasedQuery.CountAsync(cancellationToken);
        var releasedPages = Math.Max(1, (int)Math.Ceiling(releasedTotal / (double)pageSize));
        page = Math.Clamp(page, 1, releasedPages);
        var released = await releasedQuery.Include(r => r.Attempt).ThenInclude(a => a!.Exam)
            .OrderByDescending(r => r.Attempt!.ResultReleasedAtUtc)
            .ThenByDescending(r => r.ResultId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        // A pending academic result may be processing or require a Teacher decision.
        // Do not expose the withheld marks under either status.
        var pendingQuery = _context.ExamAttempts.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.ResultReleasedAtUtc == null &&
                (a.Status == AttemptStatus.ProctoringBlocked || a.Status == AttemptStatus.Completed ||
                 a.Status == AttemptStatus.AutoSubmitted || a.Status == AttemptStatus.Disqualified));
        var pendingTotal = await pendingQuery.CountAsync(cancellationToken);
        var pendingPages = Math.Max(1, (int)Math.Ceiling(pendingTotal / (double)pendingPageSize));
        pendingPage = Math.Clamp(pendingPage, 1, pendingPages);
        var pending = await pendingQuery.OrderByDescending(a => a.StartedAt)
            .ThenByDescending(a => a.AttemptId)
            .Skip((pendingPage - 1) * pendingPageSize).Take(pendingPageSize)
            .Select(a => new PendingStudentResultRow
            {
                AttemptId = a.AttemptId,
                ExamTitle = a.Exam!.ExamTitle,
                ExaminationDate = a.StartedAt,
                IsBlocked = a.Status == AttemptStatus.ProctoringBlocked,
                NeedsTeacherReview = a.Status == AttemptStatus.ProctoringBlocked ||
                    a.Status == AttemptStatus.Disqualified || a.SubmittedForViolations ||
                    _context.ProctoringLogs.Any(l => l.AttemptId == a.AttemptId &&
                        l.ParentIncidentId == null && !l.IsReviewed &&
                        l.AlertType != ProctoringAlertType.SystemAudit &&
                        l.AlertType != ProctoringAlertType.SessionRecording &&
                        l.AlertType != ProctoringAlertType.AiMonitoringUnavailable &&
                        l.AlertType != ProctoringAlertType.CameraMonitoringInterrupted &&
                        l.AlertType != ProctoringAlertType.CameraAccessLost &&
                        l.AlertType != ProctoringAlertType.MicrophoneAccessLost &&
                        l.AlertType != ProctoringAlertType.ScreenCaptureUnavailable &&
                        l.AlertType != ProctoringAlertType.AudioDetected &&
                        l.AlertType != ProctoringAlertType.TalkingDetected &&
                        l.AlertType != ProctoringAlertType.ExamBlocked &&
                        l.AlertType != ProctoringAlertType.ReviewRequested &&
                        (l.CountsAsViolation || l.PolicyPending ||
                         l.AlertType == ProctoringAlertType.AnswerPromptSpeech ||
                         l.AlertType == ProctoringAlertType.IdentityMismatch ||
                         l.AlertType == ProctoringAlertType.ObjectObservation ||
                         l.AlertType == ProctoringAlertType.AdditionalPersonObservation))
            }).ToListAsync(cancellationToken);
        return View(new StudentResultsIndexViewModel
        {
            ReleasedResults = released, PendingAttempts = pending,
            ReleasedTotal = releasedTotal, PendingTotal = pendingTotal,
            Page = page, PendingPage = pendingPage,
            ReleasedPages = releasedPages, PendingPages = pendingPages
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int? id, CancellationToken cancellationToken)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();
        if (id is null or <= 0) return ResultUnavailable();
        var result = await StudentResultAccess.ReleasedFor(_context, studentId)
            .Include(r => r.Attempt).ThenInclude(a => a!.Exam)
            .Include(r => r.Attempt).ThenInclude(a => a!.Student)
            .FirstOrDefaultAsync(r => r.ResultId == id.Value, cancellationToken);
        if (result?.Attempt?.Exam is null) return ResultUnavailable();

        ViewBag.ExamRollNumber = await _context.ExamAssignments.AsNoTracking()
            .Where(a => a.ExamId == result.Attempt.ExamId && a.StudentId == studentId)
            .Select(a => a.RollNumber).FirstOrDefaultAsync(cancellationToken);
        ViewBag.AnswerRows = await AnswerRowsAsync(result);
        return View("~/Areas/Student/Views/Result/Details.cshtml", result);
    }

    [HttpGet]
    public async Task<IActionResult> QuestionReview(int id, CancellationToken cancellationToken)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();
        var result = await StudentResultAccess.ReleasedFor(_context, studentId)
            .Include(r => r.Attempt).ThenInclude(a => a!.Exam)
            .FirstOrDefaultAsync(r => r.ResultId == id, cancellationToken);
        if (result?.Attempt?.Exam is null) return NotFound();
        ViewBag.Exam = result.Attempt.Exam;
        ViewBag.Result = result;
        return View(await AnswerRowsAsync(result));
    }

    private IActionResult ResultUnavailable()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("ResultUnavailable");
    }

    [HttpGet]
    public async Task<IActionResult> Integrity(int id, CancellationToken cancellationToken)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();
        var result = await StudentResultAccess.ReleasedFor(_context, studentId)
            .Include(r => r.Attempt).ThenInclude(a => a!.Exam)
            .FirstOrDefaultAsync(r => r.ResultId == id, cancellationToken);
        if (result?.Attempt?.Exam is null) return NotFound();

        // Hide unrelated system audit records and any other attempt's incidents.
        ViewBag.ProctoringLogs = await _context.ProctoringLogs.AsNoTracking()
            .Where(l => l.AttemptId == result.AttemptId &&
                l.StudentId == studentId && l.AlertType != ProctoringAlertType.SystemAudit)
            .OrderByDescending(l => l.CreatedDate)
            .ThenByDescending(l => l.LogId)
            .Take(100)
            .ToListAsync(cancellationToken);
        return View("Integrity", result);
    }

    private async Task<List<AiExamProctoring.ViewModels.Student.ResultAnswerRow>> AnswerRowsAsync(ExamResult result)
    {
        var sessions = await _context.ExamSessionAttempts.AsNoTracking().Include(x => x.Session)
            .Where(x => x.AttemptId == result.AttemptId).OrderBy(x => x.Session!.DisplayOrder).ToListAsync(HttpContext.RequestAborted);
        var answers = await _context.StudentAnswers.AsNoTracking().Include(x => x.Question)
            .Where(x => x.SessionAttempt!.AttemptId == result.AttemptId).ToListAsync(HttpContext.RequestAborted);
        var questions = await _context.ExamQuestions.AsNoTracking().Include(x => x.Question)
            .Where(x => x.ExamId == result.Attempt!.ExamId).OrderBy(x => x.DisplayOrder).ToListAsync(HttpContext.RequestAborted);
        var rows = new List<AiExamProctoring.ViewModels.Student.ResultAnswerRow>();
        foreach (var session in sessions)
        {
            var included = new HashSet<int>();
            foreach (var question in questions.Where(x => x.SessionId == session.SessionId))
            {
                included.Add(question.QuestionId);
                var answer = answers.FirstOrDefault(x => x.SessionAttemptId == session.SessionAttemptId && x.QuestionId == question.QuestionId);
                rows.Add(AiExamProctoring.ViewModels.Student.ResultAnswerRow.From(question.Question, answer, session.SessionAttemptId, result.Attempt!.Exam!.ShuffleOptions));
            }
            foreach (var answer in answers.Where(x => x.SessionAttemptId == session.SessionAttemptId && !included.Contains(x.QuestionId)))
                rows.Add(AiExamProctoring.ViewModels.Student.ResultAnswerRow.From(answer.Question, answer, session.SessionAttemptId, result.Attempt!.Exam!.ShuffleOptions));
        }
        return rows;
    }

    [HttpGet]
    public async Task<IActionResult> DownloadPdf(int id, CancellationToken cancellationToken)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();
        var result = await StudentResultAccess.ReleasedFor(_context, studentId)
            .Include(r => r.Attempt).ThenInclude(a => a!.Exam)
            .Include(r => r.Attempt).ThenInclude(a => a!.Student)
            .FirstOrDefaultAsync(r => r.ResultId == id, cancellationToken);
        if (result?.Attempt?.Exam is null || result.Attempt.Student is null) return NotFound();

        var rollNumber = await _context.ExamAssignments.AsNoTracking()
            .Where(a => a.ExamId == result.Attempt.ExamId && a.StudentId == studentId)
            .Select(a => a.RollNumber).FirstOrDefaultAsync(cancellationToken);
        var bytes = _pdf.Generate(result, rollNumber);
        var safeName = string.Concat(result.Attempt.Exam.ExamTitle
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')).Trim();
        Response.Headers.CacheControl = "private,no-store,max-age=0";
        return File(bytes, "application/pdf", $"{(safeName.Length == 0 ? "Exam" : safeName)}_Result.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> DownloadCertificate(int id, CancellationToken cancellationToken)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();
        var configuration = HttpContext.RequestServices.GetService<IConfiguration>();
        // Never trust an arbitrary Host header for a signed certificate destination.
        var configured = configuration?["Certificate:PublicOrigin"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            !string.IsNullOrEmpty(origin.UserInfo) || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            return Conflict("Configure Certificate:PublicOrigin with the application's trusted HTTPS origin before downloading certificates.");
        ExamResult result;
        try { result = await CertificateRecords.IssueAsync(_context, id, studentId, cancellationToken); }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        if (CertificateRecords.Status(result) != "Valid") return Conflict("This certificate is revoked or expired.");
        var photo = await _context.AttemptIdentityPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.AttemptId == result.AttemptId, cancellationToken);
        if (photo != null && !CertificateRecords.ValidPhoto(photo)) return Conflict("Original photograph integrity check failed.");
        var protector = HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(CertificateRecords.TokenPurpose);
        var token = protector.Protect(result.CertificateReference!);
        var verificationUrl = origin.GetLeftPart(UriPartial.Authority) + "/certificate/verify?token=" + Uri.EscapeDataString(token);
        byte[] bytes;
        try { bytes = _certificatePdf.Generate(result, [], photo?.ImageBytes, null, verificationUrl,
            agencyName: configuration?["Certificate:AgencyName"]); }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
        { return Conflict("The original certificate record or photograph cannot be rendered. Contact the examination owner."); }
        Response.Headers.CacheControl = "private,no-store,max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, "application/pdf", $"Result-{result.CertificateNumber}.pdf");
    }

}
