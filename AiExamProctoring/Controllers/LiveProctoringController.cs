using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Controllers;

[Authorize(Roles = "Student,Teacher")]
[Route("live-proctoring")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LiveProctoringController(ApplicationDbContext db, LiveSignalStore signals, IConfiguration config) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsTeacher => User.IsInRole("Teacher");

    [HttpGet("configuration")]
    public IActionResult Configuration() => Json(new {
        maxViewersPerCandidate = Math.Clamp(config.GetValue<int?>("LiveProctoring:MaxViewersPerCandidate") ?? 8, 1, 16),
        iceServers = config.GetSection("LiveProctoring:IceServers").Get<LiveIceServer[]>() ?? []
    });

    private Task<bool> CanAccess(int attemptId) => db.ExamAttempts.AsNoTracking().AnyAsync(a =>
        a.AttemptId == attemptId && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked) &&
        (IsTeacher ? (a.Exam!.CreatedBy == UserId || db.ExamInvigilators.Any(i => i.ExamId == a.ExamId && i.TeacherId == UserId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)) : a.StudentId == UserId), HttpContext.RequestAborted);

    [HttpPost("signal"), ValidateAntiForgeryToken, RequestSizeLimit(40_000)]
    public async Task<IActionResult> Send(int attemptId, string viewer, string kind, string? data = null)
    {
        data ??= string.Empty;
        if (!Guid.TryParseExact(viewer, "N", out _) || data.Length > 32_000 ||
            !(IsTeacher ? new[] { "watch", "answer", "ice", "bye" } : new[] { "offer", "ice", "bye" }).Contains(kind))
            return BadRequest();
        if (!await CanAccess(attemptId)) return NotFound();
        var owner = await db.ExamAttempts.Where(a => a.AttemptId == attemptId)
            .Select(a => a.Exam!.CreatedBy).SingleAsync(HttpContext.RequestAborted);
        if (IsTeacher && !signals.BindViewer(attemptId, viewer, UserId)) return Conflict();
        if (!IsTeacher) {
            owner = signals.ViewerTeacher(attemptId, viewer);
            if (owner is null || !await db.ExamAttempts.AnyAsync(a => a.AttemptId == attemptId && (a.Exam!.CreatedBy == owner || db.ExamInvigilators.Any(i => i.ExamId == a.ExamId && i.TeacherId == owner && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)))) return NotFound();
        }
        var channel = IsTeacher ? $"student:{attemptId}" : $"teacher:{owner}:{viewer}";
        return signals.Send(channel, new(attemptId, viewer, kind, data, DateTime.UtcNow.AddSeconds(30)))
            ? Ok() : StatusCode(429);
    }

    [HttpPost("poll"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Poll(int attemptId, string? viewer)
    {
        if (IsTeacher)
        {
            if (!Guid.TryParseExact(viewer, "N", out _)) return BadRequest();
            return Json(signals.Read($"teacher:{UserId}:{viewer}"));
        }
        if (!await CanAccess(attemptId)) return NotFound();
        return Json(signals.Read($"student:{attemptId}"));
    }
}

public sealed class LiveIceServer
{
    public string[] Urls { get; set; } = [];
    public string? Username { get; set; }
    public string? Credential { get; set; }
}
