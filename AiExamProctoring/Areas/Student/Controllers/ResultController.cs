using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;

namespace AiExamProctoring.Areas.Student.Controllers;

[Area("Student")]
[Authorize(Roles="Student")]
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
    public async Task<IActionResult> Index()
    {
        if(StudentId is null)return Challenge();
        var results=await _context.ExamResults.AsNoTracking().Where(r => r.Attempt!.ResultReleasedAtUtc != null ||
                (!r.Attempt.SubmittedForViolations && r.Attempt.Status != AttemptStatus.ProctoringBlocked &&
                 !_context.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation))).Include(x=>x.Attempt).ThenInclude(x=>x!.Exam)
            .Where(x=>x.Attempt!.StudentId==StudentId).OrderByDescending(x=>x.GeneratedDate).ToListAsync();
        ViewBag.PendingCount = await _context.ExamResults.CountAsync(r => r.Attempt!.StudentId == StudentId && r.Attempt.ResultReleasedAtUtc == null &&
            (r.Attempt.SubmittedForViolations || _context.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation)));
        return View(results);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int? id)
    {
        if (StudentId is null) return Challenge();
        if (id is null or <= 0) return ResultUnavailable();
        var result=await _context.ExamResults.AsNoTracking().Where(r => r.Attempt!.ResultReleasedAtUtc != null ||
                (!r.Attempt.SubmittedForViolations && r.Attempt.Status != AttemptStatus.ProctoringBlocked &&
                 !_context.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation)))
            .Include(x=>x.Attempt).ThenInclude(x=>x!.Exam)
            .Include(x=>x.Attempt).ThenInclude(x=>x!.Student)
            .FirstOrDefaultAsync(x=>x.ResultId==id&&x.Attempt!.StudentId==StudentId);
        if (result?.Attempt?.Exam is null) return ResultUnavailable();
        ViewBag.ExamRollNumber = await _context.ExamAssignments.AsNoTracking()
            .Where(x => x.ExamId == result.Attempt.ExamId && x.StudentId == StudentId)
            .Select(x => x.RollNumber)
            .FirstOrDefaultAsync();
        ViewBag.ProctoringLogs = await _context.ProctoringLogs.AsNoTracking()
            .Where(x => x.AttemptId == result.AttemptId)
            .OrderByDescending(x => x.CreatedDate)
            .Take(20)
            .ToListAsync();
        ViewBag.AnswerRows = await AnswerRowsAsync(result);
        return View("~/Areas/Student/Views/Result/Details.cshtml", result);
    }

    [HttpGet]
    public async Task<IActionResult> QuestionReview(int id)
    {
        var result=await _context.ExamResults.AsNoTracking().Where(r => r.Attempt!.ResultReleasedAtUtc != null ||
                (!r.Attempt.SubmittedForViolations && r.Attempt.Status != AttemptStatus.ProctoringBlocked &&
                 !_context.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation))).Include(x=>x.Attempt).ThenInclude(x=>x!.Exam)
            .FirstOrDefaultAsync(x=>x.ResultId==id&&x.Attempt!.StudentId==StudentId);
        if(result?.Attempt==null)return NotFound();
        ViewBag.Exam = result.Attempt.Exam;
        return View(await AnswerRowsAsync(result));
    }

    private IActionResult ResultUnavailable()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("ResultUnavailable");
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
    public async Task<IActionResult> DownloadPdf(int id)
    {
        var result=await _context.ExamResults.AsNoTracking().Where(r => r.Attempt!.ResultReleasedAtUtc != null ||
                (!r.Attempt.SubmittedForViolations && r.Attempt.Status != AttemptStatus.ProctoringBlocked &&
                 !_context.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation))).Include(x=>x.Attempt).ThenInclude(x=>x!.Exam)
            .Include(x=>x.Attempt).ThenInclude(x=>x!.Student)
            .FirstOrDefaultAsync(x=>x.ResultId==id&&x.Attempt!.StudentId==StudentId);
        if(result?.Attempt?.Student is null || result.Attempt.Exam is null)return NotFound();
        var attempt = result.Attempt;
        if (attempt?.Student == null)
            await _context.Entry(attempt!).Reference(x => x.Student).LoadAsync();

        var examRollNumber = await _context.ExamAssignments.AsNoTracking()
            .Where(x => x.ExamId == attempt!.ExamId && x.StudentId == StudentId)
            .Select(x => x.RollNumber)
            .FirstOrDefaultAsync();

        var bytes = _pdf.Generate(result, examRollNumber);
        var safeName = string.Concat(result.Attempt?.Exam?.ExamTitle ?? "Exam", "_Result")
            .Replace(" ", "_");
        return File(bytes, "application/pdf", $"{safeName}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> DownloadCertificate(int id, CancellationToken cancellationToken)
    {
        if (StudentId is null) return Challenge();
        var result = await _context.ExamResults.AsNoTracking().Where(r => r.Attempt!.ResultReleasedAtUtc != null ||
                (!r.Attempt.SubmittedForViolations && r.Attempt.Status != AttemptStatus.ProctoringBlocked &&
                 !_context.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation)))
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .Include(x => x.Attempt).ThenInclude(x => x!.Student)
            .FirstOrDefaultAsync(
                x => x.ResultId == id && x.Attempt!.StudentId == StudentId,
                cancellationToken);
        if (result?.Attempt?.Student is null || result.Attempt.Exam is null) return NotFound();

        var logs = await _context.ProctoringLogs.AsNoTracking()
            .Where(x => x.AttemptId == result.AttemptId)
            .OrderBy(x => x.CreatedDate)
            .ToListAsync(cancellationToken);
        var examRollNumber = await _context.ExamAssignments.AsNoTracking()
            .Where(x => x.ExamId == result.Attempt.ExamId && x.StudentId == StudentId)
            .Select(x => x.RollNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var profileImage = await _context.AttemptIdentityPhotos.AsNoTracking().Where(x => x.AttemptId == result.AttemptId).Select(x => x.ImageBytes).SingleOrDefaultAsync(cancellationToken);
        if (result.Attempt.Status != AttemptStatus.Completed) return Conflict("Complete the examination before downloading a certificate.");
        var protector = HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(AiExamProctoring.Controllers.CertificateController.Purpose);
        var token = protector.Protect(result.ResultId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var verificationUrl = Url.Action("Verify", "Certificate", new { area = "", token }, Request.Scheme);
        var teacherName = await _context.Users.AsNoTracking().Where(x => x.Id == result.Attempt.Exam.CreatedBy)
            .Select(x => x.FullName).FirstOrDefaultAsync(cancellationToken);
        var configuration = HttpContext.RequestServices.GetService<IConfiguration>();
        var agencyLogo = await ReadProfileImageAsync(configuration?["Certificate:LogoPath"], cancellationToken);
        var bytes = _certificatePdf.Generate(result, logs, profileImage, examRollNumber, verificationUrl,
            teacherName, agencyLogo, configuration?["Certificate:AgencyName"]);
        var safeTitle = string.Concat(result.Attempt.Exam.ExamTitle.Where(character =>
            char.IsLetterOrDigit(character) || character is ' ' or '-' or '_')).Trim().Replace(' ', '_');
        return File(bytes, "application/pdf", $"{safeTitle}_Certificate.pdf");
    }

    private async Task<byte[]?> ReadProfileImageAsync(string? storedPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        if (storedPath.StartsWith("/ProfileImage/", StringComparison.OrdinalIgnoreCase))
        {
            var privateName = Path.GetFileName(storedPath);
            if (string.IsNullOrWhiteSpace(privateName) || privateName != storedPath["/ProfileImage/".Length..])
                return null;
            var privatePath = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles", privateName);
            if (!System.IO.File.Exists(privatePath)) return null;
            var privateInfo = new FileInfo(privatePath);
            return privateInfo.Length is > 0 and <= 5_000_000
                ? await System.IO.File.ReadAllBytesAsync(privatePath, cancellationToken)
                : null;
        }

        if (string.IsNullOrWhiteSpace(_environment.WebRootPath)) return null;

        var relativePath = storedPath.Trim().TrimStart('~', '/', '\\')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(_environment.WebRootPath) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath))
            return null;

        var information = new FileInfo(fullPath);
        if (information.Length <= 0 || information.Length > 5_000_000)
            return null;
        return await System.IO.File.ReadAllBytesAsync(fullPath, cancellationToken);
    }
}
