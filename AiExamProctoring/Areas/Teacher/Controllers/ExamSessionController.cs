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
public class ExamSessionController : Controller
{
    private readonly ApplicationDbContext _db;

    public ExamSessionController(ApplicationDbContext db)
    {
        _db = db;
    }

    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(int examId)
    {
        if (examId <= 0)
            return NotFound();

        var exam = await _db.Exams
            .AsNoTracking()
            .Include(x => x.Sessions)
                .ThenInclude(x => x.ExamQuestions)
            .FirstOrDefaultAsync(x => x.ExamId == examId && x.CreatedBy == TeacherId);

        if (exam == null)
            return NotFound();

        ViewBag.Exam = exam;

        return View(exam.Sessions
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int examId)
    {
        if (examId <= 0)
            return NotFound();

        var exam = await _db.Exams
            .AsNoTracking()
            .Include(x => x.ExamQuestions)
                .ThenInclude(x => x.Question)
            .Include(x => x.Sessions)
            .FirstOrDefaultAsync(x => x.ExamId == examId && x.CreatedBy == TeacherId);

        if (exam == null)
            return NotFound();

        if (exam.IsLocked || exam.IsPublished)
        {
            TempData["Error"] = "Published or locked examinations cannot regenerate sessions.";
            return RedirectToAction(nameof(Index), new { examId });
        }

        if (exam.NumberOfSessions < 1 || exam.NumberOfSessions > 20)
        {
            TempData["Error"] = "Number of sessions must be between 1 and 20.";
            return RedirectToAction(nameof(Index), new { examId });
        }

        if (exam.DurationMinutes < exam.NumberOfSessions)
        {
            TempData["Error"] = $"Exam duration is {exam.DurationMinutes} minutes, but {exam.NumberOfSessions} sessions require at least one minute each.";
            return RedirectToAction(nameof(Index), new { examId });
        }

        var hasAttempts = await _db.ExamSessionAttempts
            .AsNoTracking()
            .AnyAsync(x => x.Session != null && x.Session.ExamId == examId);

        if (hasAttempts)
        {
            TempData["Error"] = "Sessions cannot be regenerated after students have attempted this examination. Create a new examination if the paper must be changed.";
            return RedirectToAction(nameof(Index), new { examId });
        }

        var selectedCount = exam.ExamQuestions.Count(x =>
            x.IsActive && x.Question != null && x.Question.IsActive);

        if (selectedCount == 0)
        {
            TempData["Error"] = "Please select active questions for the examination before generating sessions.";
            return RedirectToAction(nameof(Index), new { examId });
        }

        if (exam.NumberOfSessions > selectedCount)
        {
            TempData["Error"] = $"You selected {selectedCount} questions, but requested {exam.NumberOfSessions} sessions. Each session must contain at least one question.";
            return RedirectToAction(nameof(Index), new { examId });
        }

        // EnableRetryOnFailure is configured in Program.cs. EF Core requires any
        // explicit transaction to run as one retriable unit through the execution
        // strategy. Loading the working entities inside the delegate also makes a
        // possible retry start from a clean database state.
        var strategy = _db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var workingExam = await _db.Exams
                .Include(x => x.ExamQuestions)
                    .ThenInclude(x => x.Question)
                .Include(x => x.Sessions)
                .FirstAsync(x => x.ExamId == examId && x.CreatedBy == TeacherId);

            var selectedQuestions = workingExam.ExamQuestions
                .Where(x => x.IsActive && x.Question != null && x.Question.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.ExamQuestionId)
                .ToList();

            // FK ExamQuestion -> ExamSession is Restrict, so unlink questions first.
            foreach (var question in workingExam.ExamQuestions)
            {
                question.SessionId = null;
                question.Session = null;
            }

            await _db.SaveChangesAsync();

            if (workingExam.Sessions.Count > 0)
            {
                _db.ExamSessions.RemoveRange(workingExam.Sessions);
                await _db.SaveChangesAsync();
            }

            var totalQuestions = selectedQuestions.Count;
            var sessionCount = workingExam.NumberOfSessions;
            var baseQuestions = totalQuestions / sessionCount;
            var extraQuestions = totalQuestions % sessionCount;
            var baseMinutes = workingExam.DurationMinutes / sessionCount;
            var extraMinutes = workingExam.DurationMinutes % sessionCount;
            var currentQuestionIndex = 0;

            for (var i = 0; i < sessionCount; i++)
            {
                var questionsForThisSession = baseQuestions + (i < extraQuestions ? 1 : 0);
                var minutesForThisSession = baseMinutes + (i < extraMinutes ? 1 : 0);

                var session = new ExamSession
                {
                    ExamId = examId,
                    SessionNumber = i + 1,
                    SessionTitle = $"Session {i + 1}",
                    Instructions = "Answer all questions carefully. Your progress is saved automatically.",
                    DurationMinutes = minutesForThisSession,
                    RestAfterMinutes = i < sessionCount - 1 ? 10 : 0,
                    DisplayOrder = i + 1,
                    IsActive = true,
                    IsLocked = false,
                    CreatedDate = DateTime.Now
                };

                _db.ExamSessions.Add(session);

                var sessionQuestions = selectedQuestions
                    .Skip(currentQuestionIndex)
                    .Take(questionsForThisSession)
                    .ToList();

                foreach (var question in sessionQuestions)
                    question.Session = session;

                var subjectOrder = 1;
                foreach (var group in sessionQuestions
                             .Where(x => x.Question != null)
                             .GroupBy(x => x.Question!.SubjectId)
                             .OrderBy(x => x.Key))
                {
                    _db.ExamSessionSubjects.Add(new ExamSessionSubject
                    {
                        Session = session,
                        SubjectId = group.Key,
                        QuestionsToShow = group.Count(),
                        Marks = group.Sum(x => x.Marks),
                        DisplayOrder = subjectOrder++,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    });
                }

                currentQuestionIndex += questionsForThisSession;
            }

            workingExam.TotalQuestions = totalQuestions;
            // Total Marks is defined at exam level. Question marks were already
            // distributed from that total by the paper builder, so preserve the
            // configured exam total rather than silently changing it here.
            workingExam.Status = ExamStatus.Ready;
            workingExam.UpdatedDate = DateTime.Now;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        TempData["Success"] = $"{exam.NumberOfSessions} sessions generated successfully. {selectedCount} questions and {exam.DurationMinutes} minutes were distributed as evenly as possible.";
        return RedirectToAction(nameof(Index), new { examId });
    }
}
