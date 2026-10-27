using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Hubs;

[Authorize(Roles = "Student,Teacher")]
public sealed class ProctoringHub(ApplicationDbContext db, IProctoringStateService states, LiveSignalStore signals) : Hub
{
    public static string AttemptGroup(int id) => $"attempt:{id}";
    public static string TeacherGroup(string id) => $"teacher:{id}";
    private string UserId => Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public override async Task OnConnectedAsync()
    {
        if (Context.User!.IsInRole("Teacher")) await Groups.AddToGroupAsync(Context.ConnectionId, TeacherGroup(UserId));
        await base.OnConnectedAsync();
    }

    public async Task<ProctoringState?> JoinAttempt(int attemptId)
    {
        if (!Context.User!.IsInRole("Student")) throw new HubException("Student connection required.");
        if (!await db.ExamAttempts.AnyAsync(a => a.AttemptId == attemptId && a.StudentId == UserId, Context.ConnectionAborted))
            throw new HubException("Attempt unavailable.");
        await Groups.AddToGroupAsync(Context.ConnectionId, AttemptGroup(attemptId));
        return await states.GetAsync(attemptId, Context.ConnectionAborted);
    }

    public async Task Relay(int attemptId, string viewer, string kind, string data)
    {
        var teacher = Context.User!.IsInRole("Teacher");
        if (!Guid.TryParseExact(viewer, "N", out _) || data is null || data.Length > 32_000 ||
            !(teacher ? new[] { "watch", "answer", "ice", "bye" } : new[] { "offer", "ice", "bye" }).Contains(kind))
            throw new HubException("Invalid signal.");
        var owner = await db.ExamAttempts.Where(a => a.AttemptId == attemptId &&
            (a.Status == Models.AttemptStatus.InProgress || a.Status == Models.AttemptStatus.ProctoringBlocked) &&
            (teacher ? (a.Exam!.CreatedBy == UserId || db.ExamInvigilators.Any(i => i.ExamId == a.ExamId && i.TeacherId == UserId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)) : a.StudentId == UserId))
            .Select(a => a.Exam!.CreatedBy).SingleOrDefaultAsync(Context.ConnectionAborted);
        if (owner is null) throw new HubException("Attempt unavailable.");
        if (teacher && !signals.BindViewer(attemptId, viewer, UserId)) throw new HubException("Viewer unavailable.");
        if (!teacher) {
            owner = signals.ViewerTeacher(attemptId, viewer);
            if (owner is null || !await db.ExamAttempts.AnyAsync(a => a.AttemptId == attemptId && (a.Exam!.CreatedBy == owner || db.ExamInvigilators.Any(i => i.ExamId == a.ExamId && i.TeacherId == owner && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)))) throw new HubException("Viewer unavailable.");
        }
        var message = new LiveSignal(attemptId, viewer, kind, data, DateTime.UtcNow.AddSeconds(30));
        var channel = teacher ? $"student:{attemptId}" : $"teacher:{owner}:{viewer}";
        if (!signals.Send(channel, message)) throw new HubException("Signalling queue is busy. Retry shortly.");
        await Clients.Group(teacher ? AttemptGroup(attemptId) : TeacherGroup(owner))
            .SendAsync("VideoSignal", message, Context.ConnectionAborted);
    }
}
