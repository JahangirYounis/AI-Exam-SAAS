using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class ResultsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ResultsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // INDEX
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            int? examId,
            string? status)
        {
            // --------------------------------------------------------
            // Load results
            // --------------------------------------------------------

            var query = _context.ExamResults
                .AsNoTracking()
                .Include(x => x.Attempt)
                    .ThenInclude(a => a!.Student)
                .Include(x => x.Attempt)
                    .ThenInclude(a => a!.Exam)
                .AsQueryable();

            // --------------------------------------------------------
            // Search
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(x =>
                    (x.Attempt != null &&
                     x.Attempt.Student != null &&
                     x.Attempt.Student.FullName != null &&
                     x.Attempt.Student.FullName.Contains(search))

                    ||

                    (x.Attempt != null &&
                     x.Attempt.Exam != null &&
                     x.Attempt.Exam.ExamTitle.Contains(search)));
            }

            // --------------------------------------------------------
            // Exam Filter
            // --------------------------------------------------------

            if (examId.HasValue && examId.Value > 0)
            {
                query = query.Where(x =>
                    x.Attempt != null &&
                    x.Attempt.ExamId == examId.Value);
            }

            // --------------------------------------------------------
            // Status Filter
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (status.Equals("Passed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(x => x.IsPassed);
                }
                else if (status.Equals("Failed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(x => !x.IsPassed);
                }
            }

            // --------------------------------------------------------
            // Final Results
            // --------------------------------------------------------

            var results = await query
                .OrderByDescending(x => x.GeneratedDate)
                .ToListAsync();

            // --------------------------------------------------------
            // Dashboard Statistics
            // --------------------------------------------------------

            ViewBag.TotalExams =
                await _context.Exams.CountAsync();

            ViewBag.ActiveExams =
                await _context.Exams.CountAsync(x =>
                    x.Status == ExamStatus.Scheduled ||
                    x.Status == ExamStatus.Running);

            ViewBag.TotalSubmissions =
                results.Count;

            ViewBag.PassedCount =
                results.Count(x => x.IsPassed);

            ViewBag.FailedCount =
                results.Count(x => !x.IsPassed);

            ViewBag.PassPercentage =
                results.Count == 0
                    ? 0m
                    : Math.Round(
                        (decimal)results.Count(x => x.IsPassed)
                        / results.Count
                        * 100m,
                        2);

            // --------------------------------------------------------
            // Exam Dropdown
            // --------------------------------------------------------

            ViewBag.Exams = await _context.Exams
                .AsNoTracking()
                .OrderBy(x => x.ExamTitle)
                .Select(x => new
                {
                    x.ExamId,
                    x.ExamTitle
                })
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.SelectedExamId = examId;
            ViewBag.SelectedStatus = status;

            return View(results);
        }


        // ============================================================
        // DETAILS
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || id <= 0)
                return NotFound();

            var result = await _context.ExamResults
                .AsNoTracking()

                .Include(x => x.Attempt)
                    .ThenInclude(a => a!.Student)

                .Include(x => x.Attempt)
                    .ThenInclude(a => a!.Exam)

                .FirstOrDefaultAsync(x =>
                    x.ResultId == id.Value);

            if (result == null)
                return NotFound();

            return View(result);
        }
    }
}