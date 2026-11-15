
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var teacherId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(teacherId))
            return Challenge();

        // Only examinations owned by the logged-in teacher.
        var exams = await _db.Exams.AsNoTracking()
            .Where(e => e.CreatedBy == teacherId)
            .OrderByDescending(e => e.CreatedDate)
            .ThenByDescending(e => e.ExamId)
            .ToListAsync(cancellationToken);

        // Count only real student attempts.
        var studentAttempts = _db.ExamAttempts.AsNoTracking()
            .Where(a =>
                a.Exam != null &&
                a.Exam.CreatedBy == teacherId &&
                a.Status != AttemptStatus.NotStarted &&
                _db.UserRoles.Any(ur =>
                    ur.UserId == a.StudentId &&
                    _db.Roles.Any(r =>
                        r.Id == ur.RoleId &&
                        r.NormalizedName == "STUDENT")));

        // Unique students who attempted exams.
        var studentsTested = await studentAttempts
            .Select(a => a.StudentId)
            .Distinct()
            .CountAsync(cancellationToken);

        // Blocked students whose results are not released.
        var blockedReviewStudentIds = studentAttempts
            .Where(a =>
                a.ResultReleasedAtUtc == null &&
                a.Status == AttemptStatus.ProctoringBlocked)
            .Select(a => a.StudentId);

        // Only actionable and unreviewed incidents.
        var actionableReviewLogs = _db.ProctoringLogs
            .AsNoTracking()
            .Where(l =>
                !l.IsReviewed &&

                l.AlertType != ProctoringAlertType.SystemAudit &&
                l.AlertType != ProctoringAlertType.ExamUnblocked &&
                l.AlertType != ProctoringAlertType.ExamResumed &&
                l.AlertType != ProctoringAlertType.IdentityVerified &&
                l.AlertType != ProctoringAlertType.PolicyHoldReleased &&
                l.AlertType != ProctoringAlertType.ReminderIssued &&

                (
                    (l.CountsAsViolation && l.PolicyProcessed) ||
                    l.AlertType == ProctoringAlertType.ExamBlocked ||
                    l.AlertType == ProctoringAlertType.ReviewRequested ||
                    l.AlertType == ProctoringAlertType.AnswerPromptSpeech ||
                    l.AlertType == ProctoringAlertType.ObjectObservation ||
                    l.AlertType == ProctoringAlertType.AdditionalPersonObservation ||
                    (
                        l.AlertType == ProctoringAlertType.IdentityMismatch &&
                        l.Severity == ProctoringSeverity.High
                    )
                ));

        // Link incidents with valid student attempts.
        var logReviewStudentIds =
            from attempt in studentAttempts
            join log in actionableReviewLogs
                on new
                {
                    AttemptId = (int?)attempt.AttemptId,
                    ExamId = (int?)attempt.ExamId,
                    attempt.StudentId
                }
                equals new
                {
                    log.AttemptId,
                    log.ExamId,
                    log.StudentId
                }
            where attempt.ResultReleasedAtUtc == null
            select attempt.StudentId;

        // Count distinct students, not raw alerts.
        var studentsNeedingReview =
            await blockedReviewStudentIds
                .Union(logReviewStudentIds)
                .CountAsync(cancellationToken);

        var blockedAttempts = await studentAttempts
            .CountAsync(a =>
                a.Status == AttemptStatus.ProctoringBlocked &&
                a.ResultReleasedAtUtc == null,
                cancellationToken);

        // SQL TIMEOUT FIX:
        // Count only explicitly published results.
        // Avoid expensive correlated queries against ProctoringLogs.
        var availableResults = await (
            from result in _db.ExamResults.AsNoTracking()

            join attempt in _db.ExamAttempts.AsNoTracking()
                on result.AttemptId equals attempt.AttemptId

            join exam in _db.Exams.AsNoTracking()
                on attempt.ExamId equals exam.ExamId

            where exam.CreatedBy == teacherId &&
                  attempt.ResultReleasedAtUtc != null

            select result.ResultId

        ).CountAsync(cancellationToken);

        var now = DateTime.Now;

        // Next three upcoming examinations.
        var upcoming = exams
            .Where(e =>
                !e.IsReusable &&
                e.IsPublished &&
                e.Status != ExamStatus.Cancelled &&
                e.Status != ExamStatus.Completed &&
                e.StartDate >= now)
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.ExamId)
            .Take(3)
            .ToList();

        var model = new TeacherDashboardViewModel
        {
            TotalExams = exams.Count,

            DraftExams = exams.Count(e =>
                !e.IsPublished &&
                !e.IsLocked &&
                e.Status != ExamStatus.Cancelled &&
                e.Status != ExamStatus.Completed),

            PublishedExams = exams.Count(e => e.IsPublished),

            RunningExams = exams.Count(e =>
                e.Status == ExamStatus.Running),

            PrivateExams = exams.Count(e =>
                e.Audience == ExamAudience.Private),

            PublicExams = exams.Count(e =>
                e.Audience == ExamAudience.Public),

            AssignedStudents = await _db.ExamAssignments
                .AsNoTracking()
                .Where(a =>
                    a.Exam != null &&
                    a.Exam.CreatedBy == teacherId &&
                    a.IsActive)
                .Select(a => a.StudentId)
                .Distinct()
                .CountAsync(cancellationToken),

            Attempts = await studentAttempts
                .CountAsync(cancellationToken),

            StudentsTested = studentsTested,

            StudentsNeedingReview = studentsNeedingReview,

            AvailableResults = availableResults,

            Results = availableResults,

            // Compatibility with previous dashboard.
            // Counts students, not individual logs.
            PendingAlerts = studentsNeedingReview,

            BlockedAttempts = blockedAttempts,

            TotalQuestions = await _db.Questions
                .AsNoTracking()
                .CountAsync(q =>
                    q.CreatedBy == teacherId &&
                    q.IsActive,
                    cancellationToken),

            SharedApprovedQuestions = await _db.Questions
                .AsNoTracking()
                .CountAsync(q =>
                    q.IsActive &&
                    q.IsVerified &&
                    q.CreatedBy != teacherId,
                    cancellationToken),

            TodayExams = exams.Count(e =>
                !e.IsReusable &&
                e.IsPublished &&
                e.ExamDate.Date == now.Date &&
                e.Status != ExamStatus.Cancelled),

            RecentExams = exams.Take(2).ToList(),

            UpcomingExams = upcoming
        };

        return View(model);
    }
}
