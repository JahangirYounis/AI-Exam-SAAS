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
        var results = await _db.ExamResults.AsNoTracking()
            .Include(x => x.Attempt)!.ThenInclude(x => x!.Student)
            .Where(x => x.Attempt != null && x.Attempt.ExamId == examId)
            .ToListAsync(cancellationToken);
        return File(AiExamProctoring.Services.Pdf.ClassResultsPdf.Generate(exam.ExamTitle, results),
            "application/pdf", $"exam-{examId}-results.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? examId, string? search, CancellationToken cancellationToken)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (teacherId is null) return Challenge();
        var exams = await _db.Exams.AsNoTracking().Where(x => x.CreatedBy == teacherId)
            .OrderByDescending(x => x.CreatedDate).ToListAsync(cancellationToken);
        if (examId.HasValue && !exams.Any(x => x.ExamId == examId)) return NotFound();
        examId ??= exams.FirstOrDefault()?.ExamId;
        var query = _db.ExamResults.AsNoTracking()
            .Include(x => x.Attempt)!.ThenInclude(x => x!.Student)
            .Include(x => x.Attempt)!.ThenInclude(x => x!.Exam)
            .Where(x => x.Attempt != null && x.Attempt.Exam != null && x.Attempt.Exam.CreatedBy == teacherId);
        if (examId.HasValue) query = query.Where(x => x.Attempt!.ExamId == examId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(x => x.Attempt!.Student!.FullName.Contains(search) ||
                                     x.Attempt.Exam!.ExamTitle.Contains(search));
        }
        ViewBag.Exams = exams;
        ViewBag.ExamId = examId;
        var summary = await query.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(), Passed = g.Count(x => x.IsPassed),
            Average = g.Average(x => x.Percentage), Highest = g.Max(x => x.TotalMarksObtained)
        }).FirstOrDefaultAsync(cancellationToken);
        ViewBag.CompletedCount = summary?.Count ?? 0;
        ViewBag.PassCount = summary?.Passed ?? 0;
        ViewBag.AveragePercentage = summary?.Average ?? 0m;
        ViewBag.HighestScore = summary?.Highest ?? 0m;
        var page = int.TryParse(Request.Query["page"], out var requestedPage) ? Math.Max(1, requestedPage) : 1;
        var count = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (count + 49) / 50);
        page = Math.Min(page, pages);
        ViewBag.Page = page; ViewBag.Pages = pages; ViewBag.Count = count;
        return View(await query.OrderByDescending(x => x.TotalMarksObtained)
            .ThenBy(x => x.GeneratedDate).ThenBy(x => x.ResultId)
            .Skip((page - 1) * 50).Take(50).ToListAsync(cancellationToken));
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
        ViewBag.Logs = await _db.ProctoringLogs.AsNoTracking()
            .Where(x => x.AttemptId == result.AttemptId)
            .OrderByDescending(x => x.CreatedDate).ToListAsync(cancellationToken);
        return View(result);
    }
}
