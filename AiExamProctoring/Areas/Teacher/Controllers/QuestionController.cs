using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher"), Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QuestionController : Controller
{
    private const int PageSize = 30;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<QuestionController> _logger;
    public QuestionController(ApplicationDbContext db, ILogger<QuestionController> logger) { _db = db; _logger = logger; }
    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(int? subjectId, string? search, string source = "all", bool? active = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var teacherId = TeacherId;
        if (teacherId is null) return Challenge();
        source = source?.Trim().ToLowerInvariant() switch { "mine" => "mine", "shared" => "shared", _ => "all" };
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        page = Math.Max(1, page);

        var folders = await _db.Subjects.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SubjectName)
            .Select(x => new TeacherQuestionSubjectFolderViewModel
            {
                SubjectId = x.SubjectId,
                SubjectName = x.SubjectName,
                Description = x.Description,
                MyQuestionCount = x.Questions.Count(q => q.CreatedBy == teacherId),
                SharedQuestionCount = x.Questions.Count(q => q.IsActive && q.IsVerified && q.CreatedBy != teacherId)
            }).ToListAsync(cancellationToken);
        if (subjectId.HasValue && folders.All(x => x.SubjectId != subjectId.Value)) subjectId = null;

        var query = _db.Questions.AsNoTracking().Include(x => x.Subject)
            .Where(x => x.CreatedBy == teacherId || (x.IsActive && x.IsVerified));
        query = source switch
        {
            "mine" => query.Where(x => x.CreatedBy == teacherId),
            "shared" => query.Where(x => x.CreatedBy != teacherId && x.IsActive && x.IsVerified),
            _ => query
        };
        if (subjectId.HasValue) query = query.Where(x => x.SubjectId == subjectId.Value);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (search is not null) query = query.Where(x => x.QuestionText.Contains(search) || (x.Topic != null && x.Topic.Contains(search)) || (x.Tags != null && x.Tags.Contains(search)));

        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Min(page, totalPages);
        var questions = await query.OrderBy(x => x.Subject!.SubjectName).ThenBy(x => x.DisplayOrder).ThenBy(x => x.QuestionId)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        var rows = questions.Select(x => new TeacherQuestionRowViewModel
        {
            Question = x,
            IsMine = string.Equals(x.CreatedBy, teacherId, StringComparison.Ordinal),
            IsSystemStarter = string.Equals(x.CreatedBy, "System Seed", StringComparison.Ordinal),
            SourceLabel = string.Equals(x.CreatedBy, teacherId, StringComparison.Ordinal) ? "My question" :
                string.Equals(x.CreatedBy, "System Seed", StringComparison.Ordinal) ? "Verified starter" : "Verified shared"
        }).ToList();

        return View(new TeacherQuestionBankViewModel
        {
            Subjects = folders,
            Questions = rows,
            SubjectId = subjectId,
            Search = search,
            Source = source,
            Active = active,
            Page = page,
            TotalPages = totalPages,
            TotalMatching = total,
            MyQuestionCount = folders.Sum(x => x.MyQuestionCount),
            SharedQuestionCount = folders.Sum(x => x.SharedQuestionCount),
            ActiveSubjectCount = folders.Count
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? subjectId, CancellationToken cancellationToken, int? examId = null)
    {
        if (examId.HasValue)
        {
            var exam = await EditableExamAsync(examId.Value, cancellationToken);
            if (exam is null) return NotFound();
            if (exam.IsPublished || exam.IsLocked) return Conflict("Published or locked examinations cannot be changed.");
            ViewBag.ExamId = exam.ExamId;
            ViewBag.ExamTitle = exam.ExamTitle;
        }
        await LoadSubjectsAsync(subjectId, cancellationToken);
        return View(new Question { SubjectId = subjectId ?? 0, IsActive = true });
    }

    private Task<Exam?> EditableExamAsync(int examId, CancellationToken cancellationToken) =>
        _db.Exams.Include(x => x.ExamQuestions).Include(x => x.SubjectParts).Include(x => x.Sessions)
            .FirstOrDefaultAsync(x => x.ExamId == examId && (x.CreatedBy == TeacherId || _db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == TeacherId && i.IsActive && i.CanEditQuestions && i.AcceptedAtUtc != null)), cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("SubjectId,QuestionText,OptionA,OptionB,OptionC,OptionD,OptionE,CorrectAnswer,Topic,Tags,DisplayOrder,ImagePath,Explanation,IsActive")] Question model, CancellationToken cancellationToken, int? examId = null)
    {
        var teacherId = TeacherId;
        if (teacherId is null) return Challenge();
        if (examId.HasValue)
        {
            var exam = await EditableExamAsync(examId.Value, cancellationToken);
            if (exam is null) return NotFound();
            if (exam.IsPublished || exam.IsLocked) return Conflict("Published or locked examinations cannot be changed.");
            ViewBag.ExamId = exam.ExamId;
            ViewBag.ExamTitle = exam.ExamTitle;
            model.IsActive = true;
        }
        Normalize(model);
        await ResolveOptionalSubjectAsync(model, cancellationToken);
        if (!await _db.Subjects.AnyAsync(x => x.SubjectId == model.SubjectId && x.IsActive, cancellationToken))
            ModelState.AddModelError(nameof(model.SubjectId), "Choose a valid active subject.");
        if (await _db.Questions.AnyAsync(x => x.SubjectId == model.SubjectId && x.QuestionText == model.QuestionText && x.CreatedBy == teacherId, cancellationToken))
            ModelState.AddModelError(nameof(model.QuestionText), "This question already exists in your library.");
        if (!IsAnswerAvailable(model)) ModelState.AddModelError(nameof(model.CorrectAnswer), "The selected correct option must contain answer text.");
        if (!ModelState.IsValid) { await LoadSubjectsAsync(model.SubjectId, cancellationToken); return View(model); }
        model.CreatedBy = teacherId; model.CreatedDate = DateTime.Now; model.IsVerified = false; model.IsLocked = false;
        try
        {
            if (examId.HasValue)
            {
                return await _db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
                    var exam = await EditableExamAsync(examId.Value, cancellationToken);
                    if (exam is null) return NotFound();
                    await _db.Entry(exam).ReloadAsync(cancellationToken);
                    if (exam.IsPublished || exam.IsLocked) return Conflict("This examination has been published or locked. Its questions cannot change.");
                    _db.Questions.Add(model);
                    await _db.SaveChangesAsync(cancellationToken);
                    await new AiExamProctoring.Services.ExamPaperEditor(_db).AddAsync(exam, model);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["Success"] = "Your new MCQ was saved and added to this examination.";
                    return RedirectToAction("Index", "ExamQuestion", new { examId });
                });
            }
            _db.Questions.Add(model); await _db.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "MCQ saved in your Teacher Question Bank.";
            return RedirectToAction(nameof(Index), new { subjectId = model.SubjectId, source = "mine" });
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger.LogError(ex, "Teacher {TeacherId} could not create a question.", teacherId);
            ModelState.AddModelError(string.Empty, "The MCQ could not be saved. Try again or ask Admin to check the database connection.");
            await LoadSubjectsAsync(model.SubjectId, cancellationToken); return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var model = await _db.Questions.FirstOrDefaultAsync(x => x.QuestionId == id && x.CreatedBy == TeacherId, cancellationToken);
        if (model is null) return NotFound();
        await LoadSubjectsAsync(model.SubjectId, cancellationToken); return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("QuestionId,SubjectId,QuestionText,OptionA,OptionB,OptionC,OptionD,OptionE,CorrectAnswer,Topic,Tags,DisplayOrder,ImagePath,Explanation,IsActive")] Question model, CancellationToken cancellationToken)
    {
        var teacherId = TeacherId;
        if (teacherId is null) return Challenge();
        if (id != model.QuestionId) return NotFound();
        var existing = await _db.Questions.FirstOrDefaultAsync(x => x.QuestionId == id && x.CreatedBy == teacherId, cancellationToken);
        if (existing is null) return NotFound();
        if (existing.IsLocked) { TempData["Error"] = "A question used in a locked paper cannot be edited."; return RedirectToAction(nameof(Index), new { subjectId = existing.SubjectId, source = "mine" }); }
        Normalize(model);
        await ResolveOptionalSubjectAsync(model, cancellationToken);
        if (await _db.Questions.AnyAsync(x => x.QuestionId != id && x.SubjectId == model.SubjectId && x.QuestionText == model.QuestionText && x.CreatedBy == teacherId, cancellationToken))
            ModelState.AddModelError(nameof(model.QuestionText), "This question already exists in your library.");
        if (!await _db.Subjects.AnyAsync(x => x.SubjectId == model.SubjectId && x.IsActive, cancellationToken)) ModelState.AddModelError(nameof(model.SubjectId), "Choose a valid active subject.");
        if (!IsAnswerAvailable(model)) ModelState.AddModelError(nameof(model.CorrectAnswer), "The selected correct option must contain answer text.");
        if (!ModelState.IsValid) { await LoadSubjectsAsync(model.SubjectId, cancellationToken); return View(model); }

        existing.SubjectId = model.SubjectId; existing.QuestionText = model.QuestionText; existing.ImagePath = Clean(model.ImagePath); existing.Explanation = Clean(model.Explanation);
        existing.OptionA = model.OptionA; existing.OptionB = model.OptionB; existing.OptionC = model.OptionC; existing.OptionD = model.OptionD; existing.OptionE = model.OptionE;
        existing.CorrectAnswer = model.CorrectAnswer; existing.Topic = Clean(model.Topic); existing.Tags = Clean(model.Tags); existing.DisplayOrder = model.DisplayOrder;
        existing.IsActive = model.IsActive; existing.IsVerified = false; existing.UpdatedDate = DateTime.Now; existing.UpdatedBy = teacherId;
        await _db.SaveChangesAsync(cancellationToken); TempData["Success"] = "MCQ updated successfully.";
        return RedirectToAction(nameof(Index), new { subjectId = existing.SubjectId, source = "mine" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyToMine(int id, CancellationToken cancellationToken)
    {
        var teacherId = TeacherId;
        if (teacherId is null) return Challenge();
        var source = await _db.Questions.AsNoTracking().FirstOrDefaultAsync(x => x.QuestionId == id && x.IsActive && x.IsVerified, cancellationToken);
        if (source is null) return NotFound();
        var existingId = await _db.Questions.AsNoTracking().Where(x => x.CreatedBy == teacherId && x.SubjectId == source.SubjectId && x.QuestionText == source.QuestionText)
            .Select(x => (int?)x.QuestionId).FirstOrDefaultAsync(cancellationToken);
        if (existingId.HasValue) { TempData["Error"] = "An editable copy already exists."; return RedirectToAction(nameof(Edit), new { id = existingId.Value }); }
        var copy = new Question
        {
            SubjectId = source.SubjectId,
            QuestionText = source.QuestionText,
            ImagePath = source.ImagePath,
            Explanation = source.Explanation,
            OptionA = source.OptionA,
            OptionB = source.OptionB,
            OptionC = source.OptionC,
            OptionD = source.OptionD,
            OptionE = source.OptionE,
            CorrectAnswer = source.CorrectAnswer,
            Topic = source.Topic,
            Tags = source.Tags,
            DisplayOrder = source.DisplayOrder,
            IsActive = true,
            IsVerified = false,
            IsLocked = false,
            CreatedBy = teacherId,
            CreatedDate = DateTime.Now
        };
        _db.Questions.Add(copy); await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Editable copy added to My Questions."; return RedirectToAction(nameof(Edit), new { id = copy.QuestionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, CancellationToken cancellationToken)
    {
        var question = await _db.Questions.FirstOrDefaultAsync(x => x.QuestionId == id && x.CreatedBy == TeacherId, cancellationToken);
        if (question is null) return NotFound();
        if (question.IsLocked) { TempData["Error"] = "A question used in a locked paper cannot be changed."; return RedirectToAction(nameof(Index), new { subjectId = question.SubjectId, source = "mine" }); }
        question.IsActive = !question.IsActive; question.IsVerified = false; question.UpdatedDate = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken); TempData["Success"] = question.IsActive ? "Question enabled." : "Question archived; locked papers remain unchanged.";
        return RedirectToAction(nameof(Index), new { subjectId = question.SubjectId, source = "mine" });
    }

    private async Task ResolveOptionalSubjectAsync(Question question, CancellationToken cancellationToken)
    {
        if (question.SubjectId != 0 || !ModelState.IsValid) return;
        // The bank and exam builder require a subject relationship internally.
        // Unclassified questions share a folder; teachers can categorize later.
        question.SubjectId = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var subject = await _db.Subjects.FirstOrDefaultAsync(x => x.SubjectName == "Uncategorized", cancellationToken);
            if (subject is null)
            {
                subject = new Subject { SubjectName = "Uncategorized", Description = "Questions saved without choosing a subject.", IsActive = true };
                _db.Subjects.Add(subject);
                await _db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return subject.SubjectId;
        });
    }

    private static void Normalize(Question q)
    {
        q.QuestionText = q.QuestionText?.Trim() ?? string.Empty; q.OptionA = q.OptionA?.Trim() ?? string.Empty; q.OptionB = q.OptionB?.Trim() ?? string.Empty;
        q.OptionC = q.OptionC?.Trim() ?? string.Empty; q.OptionD = q.OptionD?.Trim() ?? string.Empty; q.OptionE = Clean(q.OptionE);
        q.CorrectAnswer = q.CorrectAnswer?.Trim().ToUpperInvariant() ?? "A"; q.Topic = Clean(q.Topic); q.Tags = Clean(q.Tags);
    }
    private static bool IsAnswerAvailable(Question q) => q.CorrectAnswer switch { "A" => q.OptionA.Length > 0, "B" => q.OptionB.Length > 0, "C" => q.OptionC.Length > 0, "D" => q.OptionD.Length > 0, "E" => !string.IsNullOrWhiteSpace(q.OptionE), _ => false };
    private async Task LoadSubjectsAsync(int? selected, CancellationToken ct) => ViewBag.Subjects = new SelectList(await _db.Subjects.AsNoTracking().Where(x => x.IsActive || x.SubjectId == selected).OrderBy(x => x.SubjectName).ToListAsync(ct), "SubjectId", "SubjectName", selected);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
