using AiExamProctoring.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
public sealed class ExamSessionSubjectController : Controller
{
    private readonly ApplicationDbContext _db;
    public ExamSessionSubjectController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(int sessionId, CancellationToken cancellationToken)
    {
        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var session = await _db.ExamSessions.AsNoTracking()
            .Include(x => x.Exam)
            .Include(x => x.ExamQuestions)
            .Include(x => x.SessionSubjects).ThenInclude(x => x.Subject)
            .FirstOrDefaultAsync(x => x.SessionId == sessionId && x.Exam!.CreatedBy == teacherId, cancellationToken);
        if (session?.Exam is null) return NotFound();
        ViewBag.Session = session;
        return View(session.SessionSubjects.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ToList());
    }
}
