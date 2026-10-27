using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels.Student;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Student.Controllers;

[Area("Student")]
[Authorize(Roles = "Student")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DashboardController> _logger;
    private readonly IWebHostEnvironment _environment;

    public DashboardController(ApplicationDbContext context, ILogger<DashboardController> logger, IWebHostEnvironment environment)
    {
        _context = context;
        _logger = logger;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var studentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();
        var ct = HttpContext.RequestAborted;
        try
        {
            var student = await _context.ApplicationUsers.AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == studentId, ct);
            if (student is null || student.IsBlocked)
            {
                await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Challenge();
            }

            var now = DateTime.Now;
            var hasIdentityPhoto = HasStoredProfilePhoto(student.ProfileImage);
            ViewBag.ProfileIncomplete = !hasIdentityPhoto || string.IsNullOrWhiteSpace(student.CNIC) ||
                string.IsNullOrWhiteSpace(student.FatherName) || string.IsNullOrWhiteSpace(student.District);
            var profileChecks = new[] {
                !string.IsNullOrWhiteSpace(student.FirstName), !string.IsNullOrWhiteSpace(student.LastName),
                !string.IsNullOrWhiteSpace(student.CNIC), !string.IsNullOrWhiteSpace(student.PhoneNumber),
                hasIdentityPhoto, !string.IsNullOrWhiteSpace(student.FatherName),
                !string.IsNullOrWhiteSpace(student.District), !string.IsNullOrWhiteSpace(student.PermanentAddress)
            };
            var model = new StudentDashboardViewModel
            {
                StudentId = student.Id,
                StudentName = string.IsNullOrWhiteSpace(student.FullName) ? "Student" : student.FullName,
                ProfileImage = student.ProfileImage,
                Department = student.Department,
                RollNumber = student.CandidateNumber,
                Batch = student.Batch,
                Greeting = GetGreeting(now),
                CurrentDate = now,
                LastLogin = student.LastLoginDate,
                MemberSince = student.CreatedDate,
                ProfileCompletionPercentage = (int)Math.Round(profileChecks.Count(check => check) * 100d / profileChecks.Length),
                HasIdentityPhoto = hasIdentityPhoto
            };

            var candidateExams = await _context.Exams.AsNoTracking()
                .Where(exam => exam.Status != ExamStatus.Cancelled && exam.Status != ExamStatus.Completed &&
                    ((exam.Audience == ExamAudience.Public && exam.IsPublished) ||
                     exam.Assignments.Any(assignment => assignment.StudentId == studentId && assignment.IsActive)))
                .Where(exam => !exam.AcademicClassId.HasValue || _context.ClassMemberships.Any(member =>
                    member.AcademicClassId == exam.AcademicClassId && member.StudentId == studentId &&
                    member.IsActive && member.AcademicClass!.IsActive))
                .OrderBy(exam => exam.ExamDate).ThenBy(exam => exam.StartTime).ThenBy(exam => exam.ExamId)
                .ToListAsync(ct);

            var attempts = _context.ExamAttempts.AsNoTracking().Where(attempt => attempt.StudentId == studentId);
            var attemptedIds = (await attempts.Select(attempt => attempt.ExamId).Distinct().ToListAsync(ct)).ToHashSet();
            var ongoingQuery = attempts.Where(attempt => attempt.Status == AttemptStatus.InProgress || attempt.Status == AttemptStatus.ProctoringBlocked);
            var ongoingIds = (await ongoingQuery.Select(attempt => attempt.ExamId).Distinct().ToListAsync(ct)).ToHashSet();
            var upcoming = new List<Exam>();
            var available = new List<Exam>();
            foreach (var exam in candidateExams)
            {
                if (ongoingIds.Contains(exam.ExamId) || attemptedIds.Contains(exam.ExamId) && !exam.AllowsRepeatAttempts) continue;
                if (!exam.IsReusable && now > exam.LatestStartDate) continue;
                if (!exam.IsPublished || !exam.IsReusable && now < exam.StartDate) upcoming.Add(exam);
                else available.Add(exam);
            }
            model.UpcomingExams = upcoming.Take(5).ToList();
            model.AvailableExams = available.Take(10).ToList();
            model.UpcomingExamCount = upcoming.Count;
            model.AvailableExamCount = available.Count;
            model.OngoingExamCount = await ongoingQuery.CountAsync(ct);
            model.OngoingAttempts = await ongoingQuery.Include(attempt => attempt.Exam)
                .OrderByDescending(attempt => attempt.StartedAt).ThenByDescending(attempt => attempt.AttemptId).Take(5).ToListAsync(ct);
            model.RecentAttempts = await attempts.Include(attempt => attempt.Exam)
                .Where(attempt => attempt.Status != AttemptStatus.InProgress && attempt.Status != AttemptStatus.ProctoringBlocked)
                .OrderByDescending(attempt => attempt.StartedAt).ThenByDescending(attempt => attempt.AttemptId)
                .Take(5).ToListAsync(ct);

            var attemptStats = await attempts.GroupBy(_ => 1).Select(group => new {
                Total = group.Count(),
                Completed = group.Count(attempt => attempt.Status == AttemptStatus.Completed || attempt.Status == AttemptStatus.AutoSubmitted)
            }).FirstOrDefaultAsync(ct);
            model.TotalAttempts = attemptStats?.Total ?? 0;
            model.CompletedExamCount = attemptStats?.Completed ?? 0;

            // Keep the same visibility policy as Student ResultController.
            // Aggregates and answer totals must not reveal a withheld result.
            var visibleResults = _context.ExamResults.AsNoTracking()
                .Where(result => result.Attempt != null && result.Attempt.StudentId == studentId &&
                    (result.Attempt.ResultReleasedAtUtc != null ||
                     (!result.Attempt.SubmittedForViolations && result.Attempt.Status != AttemptStatus.ProctoringBlocked &&
                      !_context.ProctoringLogs.Any(log => log.AttemptId == result.AttemptId && log.CountsAsViolation))));
            model.RecentResults = await visibleResults.Include(result => result.Attempt).ThenInclude(attempt => attempt!.Exam)
                .OrderByDescending(result => result.GeneratedDate).ThenByDescending(result => result.ResultId).Take(5).ToListAsync(ct);
            var resultStats = await visibleResults.GroupBy(_ => 1).Select(group => new {
                Passed = group.Count(result => result.IsPassed),
                Failed = group.Count(result => !result.IsPassed),
                Average = group.Average(result => (double)result.Percentage),
                Correct = group.Sum(result => result.CorrectAnswers),
                Wrong = group.Sum(result => result.WrongAnswers),
                Skipped = group.Sum(result => result.SkippedAnswers)
            }).FirstOrDefaultAsync(ct);
            model.PassedExamCount = resultStats?.Passed ?? 0;
            model.FailedExamCount = resultStats?.Failed ?? 0;
            model.AverageScore = Math.Round(resultStats?.Average ?? 0, 2);
            var resultCount = model.PassedExamCount + model.FailedExamCount;
            model.PassPercentage = resultCount == 0 ? 0 : Math.Round(model.PassedExamCount * 100d / resultCount, 2);
            model.CorrectAnswers = resultStats?.Correct ?? 0;
            model.WrongAnswers = resultStats?.Wrong ?? 0;
            model.SkippedQuestions = resultStats?.Skipped ?? 0;
            model.TotalQuestionsAttempted = model.CorrectAnswers + model.WrongAnswers;
            model.AccuracyPercentage = model.TotalQuestionsAttempted == 0 ? 0 :
                Math.Round(model.CorrectAnswers * 100d / model.TotalQuestionsAttempted, 2);
            model.TotalExams = attemptedIds.Union(candidateExams.Select(exam => exam.ExamId)).Count();

            var notifications = await _context.StudentNotifications.AsNoTracking().Where(notification => notification.StudentId == studentId)
                .OrderByDescending(notification => notification.CreatedDate).ThenByDescending(notification => notification.StudentNotificationId)
                .Take(8).ToListAsync(ct);
            model.Notifications = notifications.Select(notification => new StudentNotificationViewModel
            {
                Title = notification.Title,
                Message = notification.Message,
                Date = notification.CreatedDate,
                IsRead = notification.IsRead
            }).ToList();
            model.UnreadNotificationCount = await _context.StudentNotifications.AsNoTracking()
                .CountAsync(notification => notification.StudentId == studentId && !notification.IsRead, ct);
            return View("Index", model);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogError(error, "Student dashboard failed; reference {Reference}", HttpContext.TraceIdentifier);
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            ViewBag.DashboardLoadError = "Please try again or contact your examination administrator. Support reference: " + HttpContext.TraceIdentifier;
            return View("Index", (StudentDashboardViewModel?)null);
        }
    }

    private static string GetGreeting(DateTime time) => time.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening"
    };

    private bool HasStoredProfilePhoto(string? profileImage)
    {
        if (string.IsNullOrWhiteSpace(profileImage) || !profileImage.StartsWith("/ProfileImage/", StringComparison.OrdinalIgnoreCase)) return false;
        var fileName = Path.GetFileName(profileImage);
        if (fileName != profileImage["/ProfileImage/".Length..]) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png") return false;
        return System.IO.File.Exists(Path.Combine(
            AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles", fileName));
    }
}
