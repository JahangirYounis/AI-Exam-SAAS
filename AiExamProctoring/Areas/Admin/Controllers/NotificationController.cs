
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
namespace AiExamProctoring.Areas.Admin.Controllers;
[Area("Admin"), Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    static string Key(string id) => "TeacherApproval:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    async Task Sync()
    {
        var pending = (await users.GetUsersInRoleAsync("Teacher")).Where(t => !t.IsApproved && !t.IsBlocked).ToList();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync();
            if (db.Database.IsSqlServer()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id={Actor}");
            foreach (var teacher in pending)
            {
                var key = Key(teacher.Id); if (!await db.StudentNotifications.AnyAsync(n => n.StudentId == Actor && n.Type == key))
                    db.StudentNotifications.Add(new() { StudentId = Actor, Type = key, Title = "Teacher approval required", Message = $"Review the teacher account: {teacher.FullName}", CreatedDate = DateTime.UtcNow });
            }
            await db.SaveChangesAsync(); await tx.CommitAsync();
        });
    }
    [HttpGet]
    public async Task<IActionResult> Recent(bool history = false, int page = 0)
    {
        await Sync(); var q = db.StudentNotifications.AsNoTracking().Where(n => n.StudentId == Actor && n.Type.StartsWith("TeacherApproval:"));
        var unread = await q.CountAsync(n => !n.IsRead);
        var rows = await q.Where(n => history || !n.IsRead).OrderByDescending(n => n.StudentNotificationId).Skip(Math.Clamp(page, 0, 100000) * 20).Take(20)
         .Select(n => new { id = n.StudentNotificationId, title = n.Title, message = n.Message, read = n.IsRead, createdAt = n.CreatedDate, type = n.Type }).ToListAsync();
        return Json(new { unread, items = rows.Select(n => new { n.id, n.title, n.message, n.read, n.createdAt, n.type, category = "Account approval", url = Url.Action("Open", new { id = n.id }) }) });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        await Sync(); await db.StudentNotifications.Where(n => n.StudentId == Actor && !n.IsRead && n.Type.StartsWith("TeacherApproval:"))
         .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadDate, (DateTime?)DateTime.UtcNow)); return Ok();
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(int id)
    {
        await db.StudentNotifications.Where(n => n.StudentId == Actor && n.StudentNotificationId == id && n.Type.StartsWith("TeacherApproval:"))
         .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadDate, (DateTime?)DateTime.UtcNow)); return Ok();
    }
    [HttpGet]
    public async Task<IActionResult> Open(int id)
    {
        var n = await db.StudentNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.StudentNotificationId == id && n.StudentId == Actor);
        if (n is null) return NotFound();
        var teacher = (await users.GetUsersInRoleAsync("Teacher")).FirstOrDefault(t => Key(t.Id) == n.Type);
        return teacher is null ? NotFound() : RedirectToAction("Index", "Teacher", new { area = "Admin", id = teacher.Id });
    }
}