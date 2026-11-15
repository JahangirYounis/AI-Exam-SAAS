using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"),Authorize(Roles="Teacher"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class EvidenceSharingController(ApplicationDbContext db):Controller
{
 string Actor=>User.FindFirstValue(ClaimTypes.NameIdentifier)??"";
 [HttpGet]
 public async Task<IActionResult> Index(int logId,CancellationToken ct)
 {
  var log=await db.ProctoringLogs.AsNoTracking().SingleOrDefaultAsync(l=>l.LogId==logId&&l.Attempt!.Exam!.CreatedBy==Actor&&l.ExamId==l.Attempt.ExamId&&l.StudentId==l.Attempt.StudentId,ct);
  if(log==null)return NotFound();ViewBag.Log=log;
  ViewBag.Recipients=await db.Users.Where(u=>db.UserRoles.Any(ur=>ur.UserId==u.Id&&db.Roles.Any(r=>r.Id==ur.RoleId&&(r.Name=="Student"||r.Name=="Admin"))))
   .Select(u=>new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(u.FullName+" ("+u.Email+")",u.Id)).ToListAsync(ct);
  return View(await db.EvidenceGrants.AsNoTracking().Where(g=>g.LogId==logId&&g.OwnerId==Actor).OrderByDescending(g=>g.CreatedAtUtc).ToListAsync(ct));
 }
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> Grant(int logId,string recipientId,string kind,int hours,string reason,CancellationToken ct)
 {
  try{await EvidenceSharing.GrantAsync(db,logId,Actor,recipientId,kind,hours,reason,ct);}
  catch(UnauthorizedAccessException){return NotFound();}catch(ArgumentException e){return BadRequest(e.Message);}
  return RedirectToAction(nameof(Index),new{logId});
 }
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> Revoke(Guid id,CancellationToken ct)
 {
  var logId=await db.EvidenceGrants.Where(g=>g.Id==id&&g.OwnerId==Actor).Select(g=>(int?)g.LogId).SingleOrDefaultAsync(ct);if(logId==null)return NotFound();
  try{await EvidenceSharing.RevokeAsync(db,id,Actor,ct);}catch(UnauthorizedAccessException){return NotFound();}
  return RedirectToAction(nameof(Index),new{logId});
 }
}
