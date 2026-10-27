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
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (teacherId is null) return Challenge();

        var exams = await _db.Exams.AsNoTracking()
            .Where(x => x.CreatedBy == teacherId)
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync(cancellationToken);
        var examIds = _db.Exams.Where(x => x.CreatedBy == teacherId).Select(x => x.ExamId);
        var now = DateTime.Now;

        var model = new TeacherDashboardViewModel
        {
            TotalExams = exams.Count,
            DraftExams = exams.Count(x => !x.IsPublished && !x.IsLocked && x.Status != ExamStatus.Cancelled && x.Status != ExamStatus.Completed),
            PublishedExams = exams.Count(x => x.IsPublished),
            RunningExams = exams.Count(x => x.Status == ExamStatus.Running),
            PrivateExams = exams.Count(x => x.Audience == ExamAudience.Private),
            PublicExams = exams.Count(x => x.Audience == ExamAudience.Public),
            AssignedStudents = await _db.ExamAssignments.AsNoTracking()
                .Where(x => examIds.Contains(x.ExamId) && x.IsActive)
                .Select(x => x.StudentId).Distinct().CountAsync(cancellationToken),
            Attempts = await _db.ExamAttempts.AsNoTracking()
                .CountAsync(x => examIds.Contains(x.ExamId), cancellationToken),
            Results = await _db.ExamResults.AsNoTracking()
                .CountAsync(x => x.Attempt != null && examIds.Contains(x.Attempt.ExamId), cancellationToken),
            PendingAlerts = await _db.ProctoringLogs.AsNoTracking()
                .CountAsync(x => x.ExamId != null && examIds.Contains(x.ExamId.Value) && !x.IsReviewed, cancellationToken),
            TotalQuestions = await _db.Questions.AsNoTracking()
                .CountAsync(x => x.CreatedBy == teacherId && x.IsActive, cancellationToken),
            SharedApprovedQuestions = await _db.Questions.AsNoTracking()
                .CountAsync(x => x.IsActive && x.IsVerified && x.CreatedBy != teacherId, cancellationToken),
            BlockedAttempts = await _db.ExamAttempts.AsNoTracking()
                .CountAsync(x => examIds.Contains(x.ExamId) && x.Status == AttemptStatus.ProctoringBlocked, cancellationToken),
            TodayExams = exams.Count(x => !x.IsReusable && x.ExamDate.Date == now.Date && x.IsPublished && x.Status != ExamStatus.Cancelled),
            RecentExams = exams.Take(6).ToList(),
            UpcomingExams = exams.Where(x => !x.IsReusable && x.IsPublished && x.StartDate >= now && x.Status != ExamStatus.Cancelled && x.Status != ExamStatus.Completed)
                .OrderBy(x => x.StartDate).Take(5).ToList()
        };

        return View(model);
    }
}
