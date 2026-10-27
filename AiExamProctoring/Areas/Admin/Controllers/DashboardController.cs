using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public DashboardController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Admin dashboard deliberately loads only platform governance data.
        // Academic catalog and question-bank metrics belong to Teacher pages.
        var exams = await _db.Exams
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync();

        var students = await _users.GetUsersInRoleAsync("Student");
        var teachers = await _users.GetUsersInRoleAsync("Teacher");

        var recentExams = exams.Take(6).ToList();

        var recentAlerts = await _db.ProctoringLogs
            .AsNoTracking()
            .Include(x => x.Student)
            .Include(x => x.Exam)
            .OrderByDescending(x => x.CreatedDate)
            .Take(8)
            .ToListAsync();

        var totalResults = await _db.ExamResults
            .AsNoTracking()
            .CountAsync();

        var passedResults = await _db.ExamResults
            .AsNoTracking()
            .CountAsync(x => x.IsPassed);

        var totalAlerts = await _db.ProctoringLogs
            .AsNoTracking()
            .CountAsync();

        var pendingAlerts = await _db.ProctoringLogs
            .AsNoTracking()
            .CountAsync(x => !x.IsReviewed);

        var model = new AdminDashboardViewModel
        {
            TotalExams = exams.Count,
            PublishedExams = exams.Count(x => x.IsPublished),
            ActiveExams = exams.Count(x =>
                x.IsPublished &&
                x.Status is ExamStatus.Scheduled or ExamStatus.Running),

            TotalStudents = students.Count,
            PendingApproval = students.Count(x => !x.IsApproved && !x.IsBlocked),
            ActiveStudents = students.Count(x => x.IsApproved && !x.IsBlocked),
            BlockedStudents = students.Count(x => x.IsBlocked),
            TotalTeachers = teachers.Count,
            PendingTeachers = teachers.Count(x => !x.IsApproved && !x.IsBlocked),
            ActiveTeachers = teachers.Count(x => x.IsApproved && !x.IsBlocked),

            TotalAlerts = totalAlerts,
            PendingAlerts = pendingAlerts,

            TotalAttempts = await _db.ExamAttempts.AsNoTracking().CountAsync(),
            TotalResults = totalResults,
            PassedResults = passedResults,
            FailedResults = totalResults - passedResults,

            RecentExams = recentExams,
            RecentAlerts = recentAlerts
        };

        return View(model);
    }
}
