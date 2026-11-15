using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher"), Authorize(Roles="Teacher")]
public sealed class ChatInteractionsController(ApplicationDbContext db, IHubContext<TeacherCollaborationHub> hub, ILogger<ChatInteractionsController> logger) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> React(string kind, int roomId, int entryId, string emoji)
    {
        if (kind is not ("team" or "private") || roomId < 1 || entryId < 1 ||
            !new[] { "👍", "❤️", "😂", "😮", "😢", "😡" }.Contains(emoji)) return BadRequest();
        bool allowed;
        bool exists;
        if (kind == "team")
        {
            allowed = await db.Exams.AnyAsync(e => e.ExamId == roomId && (e.CreatedBy == Actor ||
                db.ExamInvigilators.Any(i => i.ExamId == roomId && i.TeacherId == Actor && i.IsActive && i.AcceptedAtUtc != null && i.RevokedAtUtc == null && (i.CanEditQuestions || i.CanMonitor))));
            exists = await db.ExamTeamMessages.AnyAsync(m => m.ExamId == roomId && m.Id == entryId);
        }
        else
        {
            allowed = await db.TeacherConnections.AnyAsync(c => c.Id == roomId && c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null && (c.SenderId == Actor || c.RecipientId == Actor));
            exists = await db.TeacherPrivateMessages.AnyAsync(m => m.ConnectionId == roomId && m.Id == entryId);
        }
        if (!allowed || !exists) return NotFound();
        await TeacherChatStore.ToggleReactionAsync(db, kind, roomId, entryId, Actor, emoji);
        var recipients = kind == "team"
            ? await db.ExamInvigilators.Where(i => i.ExamId == roomId && i.IsActive && i.AcceptedAtUtc != null && i.RevokedAtUtc == null && (i.CanEditQuestions || i.CanMonitor)).Select(i => i.TeacherId).ToListAsync()
            : await db.TeacherConnections.Where(c => c.Id == roomId).SelectMany(c => db.Users.Where(u => u.Id == c.SenderId || u.Id == c.RecipientId).Select(u => u.Id)).ToListAsync();
        if (kind == "team" && await db.Exams.Where(e => e.ExamId == roomId).Select(e => e.CreatedBy).SingleAsync() is string owner) recipients.Add(owner);
        try { await hub.Clients.Groups(recipients.Distinct().Select(id => "collaboration:" + id).ToArray())
            .SendAsync("CollaborationChanged", new { kind, roomId, entryId, reactionChanged = true }); }
        catch (Exception ex) { logger.LogWarning(ex, "Reaction saved but live update failed."); }
        return Ok(new { ok = true });
    }
}
