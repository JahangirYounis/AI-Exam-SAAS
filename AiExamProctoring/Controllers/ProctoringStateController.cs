using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiExamProctoring.Controllers;

[Authorize(Roles = "Student,Teacher"), Route("proctoring-state")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProctoringStateController(ApplicationDbContext db, IProctoringStateService states) : Controller
{
    [HttpGet("status")]
    public IActionResult Status([FromServices] IOptions<ProctoringOptions> options) => Json(new
    {
        model5 = "INTEGRATED",
        model6 = options.Value.Enabled ? "PROCTORING ACTIVE" : "PROCTORING DISABLED",
        version = options.Value.ModelVersion
    });

    [HttpGet("{attemptId:int}")]
    public async Task<IActionResult> Get(int attemptId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await db.ExamAttempts.AnyAsync(a => a.AttemptId == attemptId &&
            (a.StudentId == userId || User.IsInRole("Teacher") && (a.Exam!.CreatedBy == userId || db.ExamInvigilators.Any(i=>i.ExamId==a.ExamId&&i.TeacherId==userId&&i.IsActive&&i.CanMonitor&&i.AcceptedAtUtc!=null))), ct)) return NotFound();
        return Json(await states.GetAsync(attemptId, ct));
    }

    [Authorize(Roles = "Teacher"), HttpPost("control"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Control(int attemptId, bool resume, string? reason, CancellationToken ct)
    {
        reason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length is < 10 or > 300) return BadRequest(new { message = "Provide a review reason in 10–300 characters." });
        var state = await states.ControlAsync(attemptId, User.FindFirstValue(ClaimTypes.NameIdentifier)!, resume, reason, ct);
        return state is null ? Conflict(new { message = "Attempt unavailable or state has changed. Refresh and retry." }) : Json(state);
    }
}
