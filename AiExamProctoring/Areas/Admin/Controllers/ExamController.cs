using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers;

/// <summary>
/// Read-only governance view. Examination authoring, candidate assignment and
/// publication belong exclusively to approved Teacher accounts.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
public sealed class ExamController : Controller
{
    private readonly ApplicationDbContext _db;

    public ExamController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var query = _db.Exams.AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Post)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.ExamTitle.Contains(term) ||
                                     (x.ClassName != null && x.ClassName.Contains(term)));
        }

        var exams = await query.OrderByDescending(x => x.CreatedDate).ToListAsync(cancellationToken);
        var teacherIds = exams.Where(x => !string.IsNullOrWhiteSpace(x.CreatedBy))
            .Select(x => x.CreatedBy!).Distinct().ToList();
        ViewBag.TeacherNames = await _db.Users.AsNoTracking()
            .Where(x => teacherIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => (x.FirstName + " " + x.LastName).Trim(), cancellationToken);
        ViewBag.Search = search;
        return View(exams);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.ExamId == id, cancellationToken);
        if (exam is null) return NotFound();

        exam.SubjectParts = await _db.ExamSubjectParts.AsNoTracking()
            .Include(x => x.Subject)
            .Where(x => x.ExamId == id)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
        exam.ExamQuestions = await _db.ExamQuestions.AsNoTracking()
            .Include(x => x.Question)
            .Where(x => x.ExamId == id)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
        exam.Sessions = await _db.ExamSessions.AsNoTracking()
            .Where(x => x.ExamId == id)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
        exam.Assignments = await _db.ExamAssignments.AsNoTracking()
            .Where(x => x.ExamId == id)
            .ToListAsync(cancellationToken);

        var owner = string.IsNullOrWhiteSpace(exam.CreatedBy)
            ? null
            : await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == exam.CreatedBy, cancellationToken);
        ViewBag.TeacherName = owner is null
            ? "Unknown / legacy owner"
            : (owner.FirstName + " " + owner.LastName).Trim();
        ViewBag.AttemptCount = await _db.ExamAttempts.AsNoTracking()
            .CountAsync(x => x.ExamId == id, cancellationToken);
        ViewBag.BlockedAttemptCount = await _db.ExamAttempts.AsNoTracking()
            .CountAsync(x => x.ExamId == id && x.Status == AttemptStatus.ProctoringBlocked, cancellationToken);
        return View(exam);
    }
}
