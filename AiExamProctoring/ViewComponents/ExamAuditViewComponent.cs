using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.ViewComponents;
public sealed class ExamAuditViewComponent(ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int examId)
    {
        var actor = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actor is null) return Content("");
        var exam = await db.Exams.AsNoTracking().SingleOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null || !(HttpContext.User.IsInRole("Admin") || exam.CreatedBy == actor ||
            await db.ExamInvigilators.AnyAsync(i => i.ExamId == examId && i.TeacherId == actor && i.IsActive && i.AcceptedAtUtc != null))) return Content("");
        return View(new ExamAuditViewModel
        {
            Creator = await db.Users.Where(u => u.Id == exam.CreatedBy).Select(u => u.FullName).FirstOrDefaultAsync() ?? "Unknown",
            CreatorFirstMonitoringAtUtc = exam.CreatorFirstMonitoringAtUtc,
            CreatorLastMonitoringAtUtc = exam.CreatorLastMonitoringAtUtc,
            QuestionAuthors = await db.Users.Where(u => db.ExamQuestions.Any(q => q.ExamId == examId && q.Question != null && q.Question.CreatedBy == u.Id)).Select(u => u.FullName).Distinct().ToListAsync(),
            Selected = await db.ExamAssignments.Where(a => a.ExamId == examId && a.IsActive).Select(a => a.StudentId).Distinct().CountAsync(),
            Team = await db.ExamInvigilators.AsNoTracking().Include(i => i.Teacher).Where(i => i.ExamId == examId).OrderBy(i => i.InvitedAtUtc).ToListAsync(),
            Attempts = await db.ExamAttempts.AsNoTracking().Include(a => a.Student).Where(a => a.ExamId == examId).OrderByDescending(a => a.TotalMarksObtained).ToListAsync(),
            Events = await db.ProctoringLogs.AsNoTracking().Include(l => l.Student).Where(l => l.ExamId == examId && (l.CountsAsViolation || l.AlertType == ProctoringAlertType.ExamBlocked || l.AlertType == ProctoringAlertType.ExamUnblocked)).OrderByDescending(l => l.CreatedDate).ToListAsync()
        });
    }
}
