using AiExamProctoring.Models;
using AiExamProctoring.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher"), Authorize(Roles = "Teacher")]
public sealed class ResultsController : Controller
{
    private readonly ApplicationDbContext _db;
    public ResultsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> DownloadList(int examId, CancellationToken cancellationToken)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (teacherId is null) return Challenge();
        var exam = await _db.Exams.AsNoTracking().FirstOrDefaultAsync(
            x => x.ExamId == examId && x.CreatedBy == teacherId, cancellationToken);
        if (exam is null) return NotFound();
        var rows = await _db.ExamAttempts.AsNoTracking().Where(a => a.ExamId == examId)
            .OrderBy(a => a.Student!.FullName).ThenBy(a => a.AttemptId)
            .Select(a => new AiExamProctoring.Services.Pdf.ExaminationReportRow(a.Student!.FullName,
                _db.ExamAssignments.Where(x => x.ExamId == examId && x.StudentId == a.StudentId).Select(x => x.RollNumber).FirstOrDefault() ?? a.Student.CandidateNumber ?? "Not assigned",
                a.ResultReleasedAtUtc != null && a.Result != null ? a.Result.TotalMarksObtained : (decimal?)null,
                a.ResultReleasedAtUtc != null && a.Result != null ? a.Result.TotalMarks : (decimal?)null,
                a.ResultReleasedAtUtc != null && a.Result != null ? a.Result.Percentage : (decimal?)null,
                a.ResultReleasedAtUtc != null && a.Result != null ? a.Result.IsPassed : (bool?)null,
                a.ResultReleasedAtUtc != null && a.Result != null ? "Published" : "Withheld / not finalized"))
            .ToListAsync(cancellationToken);
        rows.AddRange(await _db.ExamAssignments.AsNoTracking().Where(a => a.ExamId == examId &&
                !_db.ExamAttempts.Any(t => t.ExamId == examId && t.StudentId == a.StudentId))
            .Select(a => new AiExamProctoring.Services.Pdf.ExaminationReportRow(a.Student!.FullName,
                a.RollNumber ?? a.Student.CandidateNumber ?? "Not assigned", null, null, null, null, "Not attempted")).ToListAsync(cancellationToken));
        rows = rows.OrderBy(r => r.Name).ThenBy(r => r.RollNumber).ToList();
        Response.Headers.CacheControl = "private,no-store,max-age=0";
        return File(AiExamProctoring.Services.Pdf.ClassResultsPdf.Generate(exam.ExamTitle, exam.ExamDate, rows),
            "application/pdf", $"exam-{examId}-results.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? examId, string? search, string status = "all",
        int page = 1, CancellationToken cancellationToken = default)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (teacherId is null) return Challenge();
        var exams = await _db.Exams.AsNoTracking().Where(e => e.CreatedBy == teacherId)
            .OrderByDescending(e => e.CreatedDate).ThenByDescending(e => e.ExamId)
            .ToListAsync(cancellationToken);
        if (examId.HasValue && exams.All(e => e.ExamId != examId.Value)) return NotFound();
        examId ??= exams.FirstOrDefault()?.ExamId;
        status = status?.Trim().ToLowerInvariant() switch
        {
            "released" => "released", "withheld" => "withheld", _ => "all"
        };
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 100) search = search[..100];

        // These are internal Teacher-only academic records. A result exists before
        // publication, but ResultReleasedAtUtc determines student visibility.
        var scoped = _db.ExamResults.AsNoTracking()
            .Where(r => r.Attempt != null && r.Attempt.Exam != null &&
                r.Attempt.Exam.CreatedBy == teacherId);
        if (examId.HasValue) scoped = scoped.Where(r => r.Attempt!.ExamId == examId.Value);
        if (search is not null) scoped = scoped.Where(r => r.Attempt!.Student!.FullName.Contains(search) ||
            (r.Attempt.Student.CandidateNumber != null && r.Attempt.Student.CandidateNumber.Contains(search)));

        // Keep KPI totals stable while the Released/Withheld tab changes.
        var summary = await scoped.GroupBy(_ => 1).Select(g => new
        {
            Graded = g.Count(),
            Released = g.Count(r => r.Attempt!.ResultReleasedAtUtc != null),
            Withheld = g.Count(r => r.Attempt!.ResultReleasedAtUtc == null),
            Passed = g.Count(r => r.Attempt!.ResultReleasedAtUtc != null && r.IsPassed),
            Average = g.Where(r => r.Attempt!.ResultReleasedAtUtc != null)
                .Average(r => (decimal?)r.Percentage)
        }).FirstOrDefaultAsync(cancellationToken);

        var filtered = status switch
        {
            "released" => scoped.Where(r => r.Attempt!.ResultReleasedAtUtc != null),
            "withheld" => scoped.Where(r => r.Attempt!.ResultReleasedAtUtc == null),
            _ => scoped
        };
        const int pageSize = 25;
        var count = await filtered.CountAsync(cancellationToken);
        var pages = Math.Max(1, (count + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pages);

        ViewBag.Exams = exams;
        ViewBag.ExamId = examId;
        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.GradedCount = summary?.Graded ?? 0;
        ViewBag.PublishedCount = summary?.Released ?? 0;
        ViewBag.PendingCount = summary?.Withheld ?? 0;
        ViewBag.PassCount = summary?.Passed ?? 0;
        ViewBag.AveragePercentage = summary?.Average ?? 0m;
        ViewBag.Page = page;
        ViewBag.Pages = pages;
        ViewBag.Count = count;
        ViewBag.PageSize = pageSize;

        var rows = await filtered
            .Include(r => r.Attempt)!.ThenInclude(a => a!.Student)
            .OrderByDescending(r => r.TotalMarksObtained)
            .ThenBy(r => r.GeneratedDate).ThenBy(r => r.ResultId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _db.ExamResults.AsNoTracking()
            .Include(x => x.Attempt)!.ThenInclude(x => x!.Student)
            .Include(x => x.Attempt)!.ThenInclude(x => x!.Exam)
            .FirstOrDefaultAsync(x => x.ResultId == id && x.Attempt!.Exam!.CreatedBy == teacherId, cancellationToken);
        if (result?.Attempt is null) return NotFound();
        ViewBag.AnswerRows = await AnswerRowsAsync(result);
        return View(result);
    }
    private async Task<List<AiExamProctoring.ViewModels.Student.ResultAnswerRow>> AnswerRowsAsync(ExamResult result)
    {
        var sessions = await _db.ExamSessionAttempts.AsNoTracking().Include(x => x.Session)
            .Where(x => x.AttemptId == result.AttemptId).OrderBy(x => x.Session!.DisplayOrder).ToListAsync(HttpContext.RequestAborted);
        var answers = await _db.StudentAnswers.AsNoTracking().Include(x => x.Question)
            .Where(x => x.SessionAttempt!.AttemptId == result.AttemptId).ToListAsync(HttpContext.RequestAborted);
        var questions = await _db.ExamQuestions.AsNoTracking().Include(x => x.Question)
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

}


