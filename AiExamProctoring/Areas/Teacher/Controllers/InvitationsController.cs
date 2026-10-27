using AiExamProctoring.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"),Authorize(Roles="Teacher")]
public sealed class InvitationsController(ApplicationDbContext db, IHubContext<TeacherCollaborationHub>? hub = null, IEmailSender? emailSender = null):Controller {
 string TeacherId=>User.FindFirstValue(ClaimTypes.NameIdentifier)!;
 [HttpGet] public async Task<IActionResult> Pending()=>Json(new{count=await db.ExamInvigilators.CountAsync(i=>i.TeacherId==TeacherId&&!i.IsActive&&i.AcceptedAtUtc==null&&i.RevokedAtUtc==null)});
 public async Task<IActionResult> Index()=>View(await db.ExamInvigilators.AsNoTracking().Include(x=>x.Exam).Where(x=>x.TeacherId==TeacherId).OrderByDescending(x=>x.InvitedAtUtc).ToListAsync());
 public async Task<IActionResult> Manage(int examId){var exam=await db.Exams.AsNoTracking().SingleOrDefaultAsync(e=>e.ExamId==examId&&e.CreatedBy==TeacherId);if(exam==null)return NotFound();ViewBag.Exam=exam;return View(await db.ExamInvigilators.Include(i=>i.Teacher).Where(i=>i.ExamId==examId).ToListAsync());}
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Invite(int examId,string email, bool canEditQuestions = false, bool canMonitor = false){
  if (!canEditQuestions && !canMonitor) return BadRequest("Select question preparation, live monitoring, or both.");
  if(!await db.Exams.AnyAsync(e=>e.ExamId==examId&&e.CreatedBy==TeacherId))return NotFound();
  var addresses = (email ?? "").Split(new[]{',',';','\n','\r'}, StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().ToUpperInvariant()).Distinct().ToArray();
  if(addresses.Length is <1 or >50) return BadRequest("Enter between 1 and 50 teacher email addresses.");
  var roleIds=db.Roles.Where(r=>r.Name=="Teacher").Select(r=>r.Id);var teacherIds=db.UserRoles.Where(r=>roleIds.Contains(r.RoleId)).Select(r=>r.UserId);
  var teachers=await db.Users.Where(u=>addresses.Contains(u.NormalizedEmail!)&&teacherIds.Contains(u.Id)&&u.IsApproved&&!u.IsBlocked&&u.Id!=TeacherId).ToListAsync();
  if(teachers.Count!=addresses.Length){TempData["Error"]="Every email must belong to another approved teacher. No invitations were sent.";return RedirectToAction(nameof(Manage),new{examId});}
  var existing=await db.ExamInvigilators.Where(i=>i.ExamId==examId).Select(i=>i.TeacherId).ToListAsync();
  var invited = teachers.Where(t => !existing.Contains(t.Id)).ToList();
  foreach(var t in invited) db.ExamInvigilators.Add(new ExamInvigilator{ExamId=examId,TeacherId=t.Id,IsActive=false,CanEditQuestions=canEditQuestions,CanMonitor=canMonitor});
  await db.SaveChangesAsync();
  foreach (var teacher in invited) {
   if (hub is not null) await hub.Clients.Group("collaboration:" + teacher.Id).SendAsync("CollaborationChanged", new { message = "You received an exam invitation. Open Exam invitations to accept." });
   if (emailSender is not null && teacher.Email is not null) {
    try { await emailSender.SendEmailAsync(teacher.Email, "Exam collaboration invitation", "You have been invited to collaborate on an examination. Sign in to your university examination portal and open Teacher / Invitations to review your permissions and accept."); }
    catch (Exception) { TempData["Error"] = "Invitation saved in the application, but its email could not be delivered. Check the mail configuration."; }
   }
  }
  return RedirectToAction(nameof(Manage),new{examId});
 }
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Accept(int id){var i=await db.ExamInvigilators.SingleOrDefaultAsync(i=>i.Id==id&&i.TeacherId==TeacherId&&i.RevokedAtUtc==null);if(i==null)return NotFound();i.AcceptedAtUtc=DateTime.UtcNow;i.IsActive=true;await db.SaveChangesAsync();return i.CanMonitor ? RedirectToAction("Live","Monitoring",new{examId=i.ExamId}) : RedirectToAction("Index","ExamQuestion",new{examId=i.ExamId});}
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Revoke(int id){var i=await db.ExamInvigilators.Include(i=>i.Exam).SingleOrDefaultAsync(i=>i.Id==id&&i.Exam!.CreatedBy==TeacherId);if(i==null)return NotFound();i.IsActive=false;i.RevokedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync();return RedirectToAction(nameof(Manage),new{examId=i.ExamId});}
 private Task<bool> TeamAccess(int examId) => db.Exams.AnyAsync(e => e.ExamId == examId && (e.CreatedBy == TeacherId || db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == TeacherId && i.IsActive && i.AcceptedAtUtc != null)));
 [HttpGet] public async Task<IActionResult> Team(int examId) {
  if (!await TeamAccess(examId)) return NotFound();
  ViewBag.ExamId = examId;
  var senders = db.ExamTeamMessages.Where(m => m.ExamId == examId).Select(m => m.SenderId);
  ViewBag.Names = await db.Users.Where(u => senders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
  return View(await db.ExamTeamMessages.AsNoTracking().Where(m => m.ExamId == examId).OrderByDescending(m => m.Id).Take(100).OrderBy(m => m.Id).ToListAsync());
 }
 [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
 public async Task<IActionResult> Messages(int examId, int after = 0) {
  if (!await TeamAccess(examId)) return NotFound();
  after = Math.Max(0, after);
  return Json(await db.ExamTeamMessages.AsNoTracking().Where(m => m.ExamId == examId && m.Id > after).OrderBy(m => m.Id).Take(100)
   .Select(m => new { kind = "team", roomId = examId, entryId = m.Id, body = m.Body, sender = db.Users.Where(u => u.Id == m.SenderId).Select(u => u.FullName).FirstOrDefault(), sentAtUtc = m.SentAtUtc }).ToListAsync());
 }
 [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Message(int examId, string body) {
  if (!await TeamAccess(examId)) return NotFound();
  body = body?.Trim() ?? "";
  if (body.Length is < 1 or > 2000) return BadRequest("Message must contain 1–2000 characters.");
  var message = new ExamTeamMessage { ExamId = examId, SenderId = TeacherId, Body = body };
  db.ExamTeamMessages.Add(message);
  await db.SaveChangesAsync();
  if (hub is not null) {
   var recipients = await db.ExamInvigilators.Where(i => i.ExamId == examId && i.IsActive && i.AcceptedAtUtc != null).Select(i => i.TeacherId).ToListAsync();
   var owner = await db.Exams.Where(e => e.ExamId == examId).Select(e => e.CreatedBy).SingleAsync();
   if (owner is not null) recipients.Add(owner);
   await hub.Clients.Groups(recipients.Distinct().Select(id => "collaboration:" + id).ToArray()).SendAsync("CollaborationChanged", new { message = "New exam team message", kind = "team", roomId = examId, entryId = message.Id, body, sender = await db.Users.Where(u => u.Id == TeacherId).Select(u => u.FullName).FirstOrDefaultAsync(), sentAtUtc = message.SentAtUtc });
  }
  return RedirectToAction(nameof(Team), new { examId });
 }
}
