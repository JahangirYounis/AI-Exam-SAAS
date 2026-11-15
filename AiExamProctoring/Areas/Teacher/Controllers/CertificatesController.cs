using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"),Authorize(Roles="Teacher")]
public sealed class CertificatesController(ApplicationDbContext db):Controller
{
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> Revoke(int id,string reason,CancellationToken ct)
 {
  try{await CertificateRecords.RevokeAsync(db,id,User.FindFirstValue(ClaimTypes.NameIdentifier)??"",reason,ct);}
  catch(UnauthorizedAccessException){return NotFound();}catch(ArgumentException e){return BadRequest(e.Message);}catch(InvalidOperationException e){return Conflict(e.Message);}
  return RedirectToAction("Details","Results",new{id});
 }
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> Approve(int id,string reason,CancellationToken ct)
 {
  var actor=User.FindFirstValue(ClaimTypes.NameIdentifier)??"";
  var attemptId=await db.ExamResults.Where(r=>r.ResultId==id&&r.Attempt!.Exam!.CreatedBy==actor).Select(r=>(int?)r.AttemptId).SingleOrDefaultAsync(ct);
  if(attemptId==null)return NotFound();
  if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length is <10 or >1000)return BadRequest("Provide a review reason of 10–1000 characters.");
  return await ExamResultFinalizationService.WriteAsync<IActionResult>(db,attemptId.Value,async()=>{
   var a=await db.ExamAttempts.Include(a=>a.Exam).SingleAsync(a=>a.AttemptId==attemptId,ct);
   if(a.Exam!.CreatedBy!=actor)return NotFound();
   if(a.ResultReleasedAtUtc==null||!a.CompletedAt.HasValue||await ExamResultFinalizationService.PendingIntegrity(db,a.AttemptId).AnyAsync(ct))return Conflict("Finalize the result and resolve outstanding evidence first.");
   if(a.ResultReviewedBy==null){
    a.ResultReviewedBy=actor;
    db.ProctoringLogs.Add(new ProctoringLog{AttemptId=a.AttemptId,ExamId=a.ExamId,StudentId=a.StudentId,AlertType=ProctoringAlertType.SystemAudit,CountsAsViolation=false,IsReviewed=true,ActionByUserId=actor,ModelName="Certificate lifecycle",Description="Approved: "+reason.Trim()[..Math.Min(reason.Trim().Length,450)],MachineObservationsJson=System.Text.Json.JsonSerializer.Serialize(new{decision="Approved",reason,actor,atUtc=DateTime.UtcNow})});
   }
   return RedirectToAction("Details","Results",new{id});
  },ct);
 }
}
