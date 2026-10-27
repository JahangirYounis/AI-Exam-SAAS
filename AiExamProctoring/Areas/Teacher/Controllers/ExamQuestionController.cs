using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ExamQuestionController : Controller
{
    private readonly ApplicationDbContext _db;

    public ExamQuestionController(ApplicationDbContext db) => _db = db;

    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(int examId, int? subjectId, string? search, int page = 1)
    {
        if (examId <= 0) return NotFound();

        const int pageSize = 30;
        page = Math.Max(1, page);

        var exam = await _db.Exams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExamId == examId && (x.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == TeacherId && i.IsActive && i.CanEditQuestions && i.AcceptedAtUtc != null)));
        if (exam is null) return NotFound();

        var subjects = await _db.Subjects.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SubjectName)
            .ToListAsync();

        var subjectCounts = await _db.Questions.AsNoTracking()
            .Where(x => x.IsActive && (x.CreatedBy == TeacherId || x.CreatedBy == exam.CreatedBy || x.IsVerified || _db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == x.CreatedBy && i.IsActive && i.CanEditQuestions && i.AcceptedAtUtc != null)) &&
                        !x.ExamQuestions.Any(eq => eq.ExamId == examId && eq.IsActive))
            .GroupBy(x => x.SubjectId)
            .Select(g => new { SubjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count);

        var selectedCount = await _db.ExamQuestions.AsNoTracking()
            .CountAsync(x => x.ExamId == examId && x.IsActive);

        var query = _db.Questions.AsNoTracking()
            .Include(x => x.Subject)
            .Where(x => x.IsActive && (x.CreatedBy == TeacherId || x.CreatedBy == exam.CreatedBy || x.IsVerified || _db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == x.CreatedBy && i.IsActive && i.CanEditQuestions && i.AcceptedAtUtc != null)) &&
                        !x.ExamQuestions.Any(eq => eq.ExamId == examId && eq.IsActive));

        if (subjectId.HasValue)
            query = query.Where(x => x.SubjectId == subjectId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(x => x.QuestionText.Contains(search) ||
                                     (x.Topic != null && x.Topic.Contains(search)) ||
                                     (x.Tags != null && x.Tags.Contains(search)));
        }

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        if (page > totalPages) page = totalPages;

        var questions = await query
            .OrderBy(x => x.Subject!.SubjectName)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.QuestionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Exam = exam;
        ViewBag.Subjects = subjects;
        ViewBag.SubjectCounts = subjectCounts;
        ViewBag.SubjectId = subjectId;
        ViewBag.SelectedCount = selectedCount;
        ViewBag.Search = search;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalQuestions = total;
        ViewBag.PageSize = pageSize;
        return View(questions);
    }

    [HttpGet]
    public async Task<IActionResult> Selected(int examId)
    {
        if (examId <= 0) return NotFound();
        var exam = await _db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.ExamId == examId && (x.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == TeacherId && i.IsActive && i.CanEditQuestions && i.AcceptedAtUtc != null)));
        if (exam is null) return NotFound();

        var selected = await _db.ExamQuestions.AsNoTracking()
            .Include(x => x.Question)!.ThenInclude(x => x!.Subject)
            .Where(x => x.ExamId == examId && x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();

        ViewBag.Exam = exam;
        return View(selected);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> AddQuestion(int examId, int questionId) => ChangeSelection(examId, questionId, true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveQuestion(int examId, int questionId) => ChangeSelection(examId, questionId, false);

    private async Task<IActionResult> ChangeSelection(int examId, int questionId, bool add)
    {
        var teacherId = TeacherId;
        if (string.IsNullOrWhiteSpace(teacherId)) return Unauthorized();
        if (examId <= 0 || questionId <= 0) return BadRequest();
        var ct = HttpContext.RequestAborted;
        var execution = 0;
        try
        {
            return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (execution++ > 0) _db.ChangeTracker.Clear();
                await using var transaction = _db.Database.IsRelational()
                    ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
                var exam = await _db.Exams.AsSplitQuery()
                    .Include(x => x.ExamQuestions).Include(x => x.SubjectParts).Include(x => x.Sessions)
                    .FirstOrDefaultAsync(x => x.ExamId == examId && (x.CreatedBy == teacherId || _db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == teacherId && i.IsActive && i.CanEditQuestions && i.AcceptedAtUtc != null)), ct);
                if (exam is null) return (IActionResult)NotFound();
                await _db.Entry(exam).ReloadAsync(ct);
                if (exam.IsLocked || exam.IsPublished || exam.Status == ExamStatus.Cancelled || exam.Status == ExamStatus.Completed ||
                    await _db.ExamAttempts.AnyAsync(x => x.ExamId == examId, ct))
                    return Json(new { success = false, message = "This test is locked or already has attempts. Its questions cannot change." });
                var selected = exam.ExamQuestions.FirstOrDefault(x => x.QuestionId == questionId && x.IsActive);
                if (add)
                {
                    if (selected is null)
                    {
                        var question = await _db.Questions.AsNoTracking().FirstOrDefaultAsync(x => x.QuestionId == questionId &&
                            x.IsActive && (x.CreatedBy == teacherId || x.CreatedBy == exam.CreatedBy || x.IsVerified || _db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == x.CreatedBy && i.IsActive && i.CanEditQuestions && i.AcceptedAtUtc != null)), ct);
                        if (question is null) return Json(new { success = false, message = "This question is not available to your question bank." });
                        await new AiExamProctoring.Services.ExamPaperEditor(_db).AddAsync(exam, question);
                    }
                }
                else if (selected is not null)
                {
                    _db.ExamQuestions.Remove(selected);
                    exam.ExamQuestions.Remove(selected);
                    await SavePaperChangeAsync(exam);
                }
                if (transaction is not null) await transaction.CommitAsync(ct);
                return Json(BuildSelectionResponse(exam, add ? "Question selected." : "Question removed."));
            });
        }
        catch (InvalidOperationException)
        {
            return Json(new { success = false, message = "The paper could not be updated. Check the selected question count and total marks, then retry." });
        }
        catch (DbUpdateException)
        {
            return Json(new { success = false, message = "The paper changed while saving. Reload the test and try again." });
        }
    }

    private Task SavePaperChangeAsync(Exam exam) =>
        new AiExamProctoring.Services.ExamPaperEditor(_db).SavePaperChangeAsync(exam);

    private static object BuildSelectionResponse(Exam exam, string message)
    {
        var count = exam.ExamQuestions.Count(x => x.IsActive);
        return new
        {
            success = true,
            message,
            selectedCount = count,
            totalMarks = exam.TotalMarks,
            marksPerQuestion = count == 0 ? 0m : Math.Round(exam.TotalMarks / count, 4)
        };
    }
}
