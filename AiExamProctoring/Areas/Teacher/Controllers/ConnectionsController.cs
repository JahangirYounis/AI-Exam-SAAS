using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AiExamProctoring.ViewModels;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"), Authorize(Roles = "Teacher")]
public sealed class ConnectionsController(ApplicationDbContext db, IHubContext<TeacherCollaborationHub> hub, ILogger<ConnectionsController> logger) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private Task Notify(string recipient, string message) => hub.Clients.Group("collaboration:" + recipient).SendAsync("CollaborationChanged", new { message });
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(string? search, string tab = "directory", CancellationToken ct = default)
    {
        search = (search ?? "").Trim();
        if (search.Length > 100) search = search[..100];
        tab = tab is "received" or "sent" or "connected" ? tab : "directory";
        var roleIds = db.Roles.Where(r => r.Name == "Teacher").Select(r => r.Id);
        var directory = db.Users.AsNoTracking().Where(u => u.Id != Actor && u.IsApproved && !u.IsBlocked &&
            db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)));
        if (search.Length > 0) directory = directory.Where(u => u.FullName.Contains(search) ||
            (u.Department != null && u.Department.Contains(search)) ||
            (u.OrganizationName != null && u.OrganizationName.Contains(search)));
        var connections = await db.TeacherConnections.AsNoTracking()
            .Where(c => c.SenderId == Actor || c.RecipientId == Actor).ToListAsync(ct);
        var people = await directory.OrderBy(u => u.FullName).Take(100)
            .Select(u => new { u.Id, u.FullName, u.ProfileImage, u.Department, u.OrganizationName, u.City })
            .ToListAsync(ct);
        var cards = people.Select(u => {
            var c = connections.FirstOrDefault(x => x.SenderId == u.Id || x.RecipientId == u.Id);
            var status = c is null ? "none" : c.ClosedAtUtc != null ? "removed" : c.DeclinedAtUtc != null ? "declined" :
                c.AcceptedAtUtc != null ? "connected" : c.RecipientId == Actor ? "received" : "sent";
            return new TeacherDirectoryPersonViewModel {
                Id = u.Id, FullName = u.FullName, ProfileImage = u.ProfileImage, Department = u.Department,
                OrganizationName = u.OrganizationName, City = u.City, ConnectionId = c?.Id, Status = status
            };
        }).Where(c => tab switch {
            "received" => c.Status == "received",
            "sent" => c.Status == "sent",
            "connected" => c.Status == "connected",
            _ => true
        }).ToList();
        var active = connections.Where(c => c.ClosedAtUtc is null && c.DeclinedAtUtc is null).ToList();
        return View(new TeacherDirectoryViewModel {
            People = cards, Search = search, Tab = tab,
            ReceivedCount = active.Count(c => c.AcceptedAtUtc is null && c.RecipientId == Actor),
            SentCount = active.Count(c => c.AcceptedAtUtc is null && c.SenderId == Actor),
            ConnectedCount = active.Count(c => c.AcceptedAtUtc != null)
        });
    }

    private void Log(TeacherConnection connection, string action) =>
        db.TeacherConnectionEvents.Add(new TeacherConnectionEvent {
            ConnectionId = connection.Id, ActorId = Actor, Action = action, OccurredAtUtc = DateTime.UtcNow
        });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(string teacherId, int? examId = null, bool monitoringOnly = false, CancellationToken ct = default)
    {
        var roleIds = db.Roles.Where(r => r.Name == "Teacher").Select(r => r.Id);
        if (string.IsNullOrEmpty(teacherId) || teacherId == Actor ||
            !await db.Users.AnyAsync(u => u.Id == teacherId && u.IsApproved && !u.IsBlocked &&
                db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)), ct)) return NotFound();
        var pair = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("|", new[] { Actor, teacherId }.OrderBy(x => x, StringComparer.Ordinal)))));
        var existing = await db.TeacherConnections.SingleOrDefaultAsync(c => c.PairKey == pair, ct);
        if (existing?.AcceptedAtUtc != null && existing.ClosedAtUtc is null && existing.DeclinedAtUtc is null)
            return RedirectToAction(nameof(Chat), new { id = existing.Id });
        if (existing is not null && existing.ClosedAtUtc is null && existing.DeclinedAtUtc is null)
            return RedirectToAction(nameof(Index), new { tab = existing.RecipientId == Actor ? "received" : "sent" });
        if (existing?.DeclinedAtUtc is DateTime declined && declined.AddDays(7) > DateTime.UtcNow)
            return Conflict("This request was declined recently. Please allow seven days before sending a new invitation.");
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            await using var trx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var record = await db.TeacherConnections.SingleOrDefaultAsync(c => c.PairKey == pair, ct);
            if (record is null) {
                record = new TeacherConnection { PairKey = pair, SenderId = Actor, RecipientId = teacherId, CreatedAtUtc = DateTime.UtcNow };
                db.TeacherConnections.Add(record);
                await db.SaveChangesAsync(ct);
                Log(record, "Requested");
            } else if (record.ClosedAtUtc != null || record.DeclinedAtUtc != null) {
                record.SenderId = Actor; record.RecipientId = teacherId; record.AcceptedAtUtc = null;
                record.ClosedAtUtc = null; record.DeclinedAtUtc = null; record.CreatedAtUtc = DateTime.UtcNow;
                Log(record, "RequestedAgain");
            }
            await db.SaveChangesAsync(ct);
            await trx.CommitAsync(ct);
        });
        try { await Notify(teacherId, "A teacher sent you a connection request."); }
        catch (Exception ex) { logger.LogWarning(ex, "Connection notification delivery failed."); }
        if (examId.HasValue && await db.Exams.AnyAsync(e => e.ExamId == examId && e.CreatedBy == Actor, ct))
            return RedirectToAction("Manage", "Invitations", new { examId, monitoringOnly });
        return RedirectToAction(nameof(Index), new { tab = "sent" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id, CancellationToken ct)
    {
        var connection = await db.TeacherConnections.SingleOrDefaultAsync(c => c.Id == id && c.RecipientId == Actor &&
            c.ClosedAtUtc == null && c.DeclinedAtUtc == null, ct);
        if (connection is null) return NotFound();
        if (!connection.AcceptedAtUtc.HasValue) {
            connection.AcceptedAtUtc = DateTime.UtcNow; Log(connection, "Accepted");
            await db.SaveChangesAsync(ct);
            try { await Notify(connection.SenderId, "Your teacher connection request was accepted."); }
            catch (Exception ex) { logger.LogWarning(ex, "Connection acceptance notification failed."); }
        }
        return RedirectToAction(nameof(Chat), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id, CancellationToken ct)
    {
        var connection = await db.TeacherConnections.SingleOrDefaultAsync(c => c.Id == id && c.RecipientId == Actor &&
            c.AcceptedAtUtc == null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null, ct);
        if (connection is null) return NotFound();
        connection.DeclinedAtUtc = DateTime.UtcNow;
        Log(connection, "Declined"); await db.SaveChangesAsync(ct);
        try { await Notify(connection.SenderId, "Your teacher connection request was declined."); }
        catch (Exception ex) { logger.LogWarning(ex, "Decline notification failed."); }
        return RedirectToAction(nameof(Index), new { tab = "received" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id, CancellationToken ct)
    {
        var connection = await db.TeacherConnections.SingleOrDefaultAsync(c => c.Id == id && c.SenderId == Actor &&
            c.AcceptedAtUtc == null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null, ct);
        if (connection is null) return NotFound();
        connection.ClosedAtUtc = DateTime.UtcNow;
        Log(connection, "Withdrawn"); await db.SaveChangesAsync(ct);
        try { await Notify(connection.RecipientId, "A teacher connection request was withdrawn."); }
        catch (Exception ex) { logger.LogWarning(ex, "Withdraw notification failed."); }
        return RedirectToAction(nameof(Index), new { tab = "sent" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        var connection = await db.TeacherConnections.SingleOrDefaultAsync(c => c.Id == id &&
            (c.SenderId == Actor || c.RecipientId == Actor) && c.AcceptedAtUtc != null &&
            c.DeclinedAtUtc == null && c.ClosedAtUtc == null, ct);
        if (connection is null) return NotFound();
        connection.ClosedAtUtc = DateTime.UtcNow;
        Log(connection, "Removed"); await db.SaveChangesAsync(ct);
        var other = connection.SenderId == Actor ? connection.RecipientId : connection.SenderId;
        try { await Notify(other, "A teacher connection was closed."); }
        catch (Exception ex) { logger.LogWarning(ex, "Removal notification failed."); }
        return RedirectToAction(nameof(Index), new { tab = "connected" });
    }

    public async Task<IActionResult> Chat(int id)
    {
        var connection = await db.TeacherConnections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null && (c.SenderId == Actor || c.RecipientId == Actor));
        if (connection is null) return NotFound();
        ViewBag.ConnectionId = id; ViewBag.Actor = Actor;
        var colleagueId = connection.SenderId == Actor ? connection.RecipientId : connection.SenderId;
        ViewBag.Colleague = await db.Users.Where(u => u.Id == colleagueId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "Colleague";
        return View(await db.TeacherPrivateMessages.AsNoTracking().Where(m => m.ConnectionId == id).OrderByDescending(m => m.Id).Take(100).OrderBy(m => m.Id).ToListAsync());
    }
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Messages(int id, int after = 0)
    {
        if (!await db.TeacherConnections.AnyAsync(c => c.Id == id && c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null && (c.SenderId == Actor || c.RecipientId == Actor))) return NotFound();
        after = Math.Max(0, after);
        var query = db.TeacherPrivateMessages.AsNoTracking().Where(m => m.ConnectionId == id);
        var raw = after == 0
            ? await query.OrderByDescending(m => m.Id).Take(100).ToListAsync()
            : await query.Where(m => m.Id > after).OrderBy(m => m.Id).Take(100).ToListAsync();
        var names = await db.Users.Where(u => raw.Select(m=>m.SenderId).Contains(u.Id)).ToDictionaryAsync(u=>u.Id,u=>u.FullName);
        var entries = raw.OrderBy(m=>m.Id).Select(m => new TeacherChatEntry {
            Kind="private", RoomId=id, EntryId=m.Id, Body=m.Body, SenderId=m.SenderId,
            Sender=names.GetValueOrDefault(m.SenderId,"Teacher"), SentAtUtc=m.SentAtUtc }).ToList();
        await TeacherChatStore.EnrichAsync(db,"private",id,Actor,entries);
        foreach(var e in entries.Where(e => e.ReplyToId.HasValue)) {
            var referenced = entries.FirstOrDefault(x => x.EntryId == e.ReplyToId);
            e.ReplyBody = referenced?.Body;
            e.ReplySender = referenced?.Sender;
        }
        return Json(entries);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Message(int id, string body, int? replyToId = null)
    {
        var connection = await db.TeacherConnections.SingleOrDefaultAsync(c => c.Id == id && c.AcceptedAtUtc != null && c.DeclinedAtUtc == null && c.ClosedAtUtc == null && (c.SenderId == Actor || c.RecipientId == Actor));
        if (connection is null) return NotFound();
        body = body?.Trim() ?? "";
        if (body.Length is < 1 or > 2000) return BadRequest("Write 1–2000 characters.");
        if (replyToId.HasValue && !await db.TeacherPrivateMessages.AnyAsync(m => m.ConnectionId == id && m.Id == replyToId.Value)) return BadRequest("Reply target is not in this chat.");
        TeacherPrivateMessage message = null!;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (!await db.TeacherConnections.AnyAsync(c => c.Id == id && c.AcceptedAtUtc != null &&
                c.DeclinedAtUtc == null && c.ClosedAtUtc == null && (c.SenderId == Actor || c.RecipientId == Actor)))
                throw new InvalidOperationException("This connection is no longer active.");
            message = new TeacherPrivateMessage { ConnectionId = id, SenderId = Actor, Body = body };
            db.TeacherPrivateMessages.Add(message); await db.SaveChangesAsync();
            if (replyToId.HasValue) await TeacherChatStore.SaveReplyAsync(db,"private",id,message.Id,replyToId.Value);
            await transaction.CommitAsync();
        });
        {
            var recipient = connection.SenderId == Actor ? connection.RecipientId : connection.SenderId;
            try {
                await hub.Clients.Group("collaboration:" + recipient).SendAsync("CollaborationChanged", new { message = "New private message", kind = "private", roomId = id, entryId = message.Id, body, sender = await db.Users.Where(u => u.Id == Actor).Select(u => u.FullName).FirstOrDefaultAsync(), sentAtUtc = message.SentAtUtc });
            } catch (Exception ex) { logger.LogWarning(ex, "Private chat notification delivery failed. MessageId={MessageId}",message.Id); }
        }
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Ok(new { ok = true });
        return RedirectToAction(nameof(Chat), new { id });
    }
}
