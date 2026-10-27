using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public sealed class ExamHistoryController : Controller
{
    private readonly ApplicationDbContext _db;
    public ExamHistoryController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    [Route("/Admin/ExamHistory")]
    [Route("/Admin/ExaminationHistory")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var now = DateTime.Now;

        // SQL narrows the candidate set; EndDate is a NotMapped helper, so the final
        // elapsed-window check is intentionally performed in memory.
        var candidateQuery = _db.Exams.AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Post)
            .Where(x => x.Status == ExamStatus.Completed ||
                        (x.IsPublished && x.ExamDate <= now.Date));

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            candidateQuery = candidateQuery.Where(x => x.ExamTitle.Contains(search) ||
                                     (x.Post != null && x.Post.PostName.Contains(search)) ||
                                     (x.Department != null && x.Department.DepartmentName.Contains(search)));
        }

        var historical = (await candidateQuery.ToListAsync())
            .Where(x => x.Status == ExamStatus.Completed || now > x.EndDate)
            .OrderByDescending(x => x.ExamDate)
            .ThenByDescending(x => x.StartTime)
            .ToList();

        var total = historical.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var exams = historical.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        ViewBag.Search = search;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Total = total;
        return View(exams);
    }

    [HttpGet]
    [Route("/Admin/ExamHistory/Details/{id:int}")]
    [Route("/Admin/ExaminationHistory/Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Post)
            .Include(x => x.ExamQuestions).ThenInclude(x => x.Question).ThenInclude(x => x!.Subject)
            .Include(x => x.ExamQuestions).ThenInclude(x => x.Session)
            .Include(x => x.Sessions)
            .Include(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.ExamId == id && (x.IsPublished || x.Status == ExamStatus.Completed));
        if (exam is null) return NotFound();

        var attemptStats = await _db.ExamAttempts.AsNoTracking()
            .Where(x => x.ExamId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Attempts = g.Count(),
                Completed = g.Count(x => x.Status == AttemptStatus.Completed || x.Status == AttemptStatus.AutoSubmitted),
                Passed = g.Count(x => x.IsPassed),
                Average = g.Average(x => (double)x.Percentage)
            })
            .FirstOrDefaultAsync();

        ViewBag.Attempts = attemptStats?.Attempts ?? 0;
        ViewBag.Completed = attemptStats?.Completed ?? 0;
        ViewBag.Passed = attemptStats?.Passed ?? 0;
        ViewBag.Average = Math.Round(attemptStats?.Average ?? 0d, 2);
        return View(exam);
    }
}
