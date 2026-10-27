using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"), Authorize(Roles = "Teacher")]
public sealed class ConnectionsController(ApplicationDbContext db, IHubContext<TeacherCollaborationHub> hub) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private Task Notify(string recipient, string message) => hub.Clients.Group("collaboration:" + recipient).SendAsync("CollaborationChanged", new { message });
    public async Task<IActionResult> Index(string? search)
    {
        var roles = db.Roles.Where(r => r.Name == "Teacher").Select(r => r.Id);
        var teachers = db.Users.AsNoTracking().Where(u => u.Id != Actor && u.IsApproved && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == u.Id && roles.Contains(ur.RoleId)));
        if (!string.IsNullOrWhiteSpace(search)) teachers = teachers.Where(u => u.FullName.Contains(search));
        ViewBag.Connections = await db.TeacherConnections.AsNoTracking().Where(c => c.SenderId == Actor || c.RecipientId == Actor).ToListAsync();
        ViewBag.Actor = Actor;
        return View(await teachers.OrderBy(u => u.FullName).Take(100).ToListAsync());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(string teacherId)
    {
        var roles = db.Roles.Where(r => r.Name == "Teacher").Select(r => r.Id);
        if (teacherId == Actor || !await db.Users.AnyAsync(u => u.Id == teacherId && u.IsApproved && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == u.Id && roles.Contains(ur.RoleId)))) return NotFound();
        var pair = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|", new[] { Actor, teacherId }.OrderBy(x => x, StringComparer.Ordinal)))));
        if (!await db.TeacherConnections.AnyAsync(c => c.PairKey == pair))
        {
            db.TeacherConnections.Add(new TeacherConnection { PairKey = pair, SenderId = Actor, RecipientId = teacherId });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Conflict("A connection request already exists. Refresh the directory."); }
            await Notify(teacherId, "A teacher sent you a connection request. Open the teacher directory.");
        }
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var connection = await db.TeacherConnections.SingleOrDefaultAsync(c => c.Id == id && c.RecipientId == Actor);
        if (connection is null) return NotFound();
        connection.AcceptedAtUtc ??= DateTime.UtcNow; await db.SaveChangesAsync();
        await Notify(connection.SenderId, "Your teacher connection request was accepted.");
        return RedirectToAction(nameof(Chat), new { id });
    }
    public async Task<IActionResult> Chat(int id)
    {
        var connection = await db.TeacherConnections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.AcceptedAtUtc != null && (c.SenderId == Actor || c.RecipientId == Actor));
        if (connection is null) return NotFound();
        ViewBag.ConnectionId = id; ViewBag.Actor = Actor;
        return View(await db.TeacherPrivateMessages.AsNoTracking().Where(m => m.ConnectionId == id).OrderByDescending(m => m.Id).Take(100).OrderBy(m => m.Id).ToListAsync());
    }
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Messages(int id, int after = 0)
    {
        if (!await db.TeacherConnections.AnyAsync(c => c.Id == id && c.AcceptedAtUtc != null && (c.SenderId == Actor || c.RecipientId == Actor))) return NotFound();
        after = Math.Max(0, after);
        return Json(await db.TeacherPrivateMessages.AsNoTracking().Where(m => m.ConnectionId == id && m.Id > after).OrderBy(m => m.Id).Take(100)
            .Select(m => new { kind = "private", roomId = id, entryId = m.Id, body = m.Body, sender = db.Users.Where(u => u.Id == m.SenderId).Select(u => u.FullName).FirstOrDefault(), sentAtUtc = m.SentAtUtc }).ToListAsync());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Message(int id, string body)
    {
        var connection = await db.TeacherConnections.SingleOrDefaultAsync(c => c.Id == id && c.AcceptedAtUtc != null && (c.SenderId == Actor || c.RecipientId == Actor));
        if (connection is null) return NotFound();
        body = body?.Trim() ?? "";
        if (body.Length is < 1 or > 2000) return BadRequest("Write 1–2000 characters.");
        var message = new TeacherPrivateMessage { ConnectionId = id, SenderId = Actor, Body = body };
        db.TeacherPrivateMessages.Add(message); await db.SaveChangesAsync();
        var recipient = connection.SenderId == Actor ? connection.RecipientId : connection.SenderId;
        await hub.Clients.Group("collaboration:" + recipient).SendAsync("CollaborationChanged", new { message = "New private message", kind = "private", roomId = id, entryId = message.Id, body, sender = await db.Users.Where(u => u.Id == Actor).Select(u => u.FullName).FirstOrDefaultAsync(), sentAtUtc = message.SentAtUtc });
        return RedirectToAction(nameof(Chat), new { id });
    }
}
