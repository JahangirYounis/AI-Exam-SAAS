using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Results;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace AiExamProctoring.Services.Proctoring;
public static class EvidenceSharing
{
 public static string? Key(ProctoringLog log,string kind)=>kind switch {
  "screenshot"=>log.ScreenshotPath,"video"=>log.VideoEvidencePath,
  "recording"=>log.RecordingPath??(log.ScreenshotPath?.EndsWith(".webm",StringComparison.OrdinalIgnoreCase)==true?log.ScreenshotPath:null),_=>null};
 public static async Task<Guid> GrantAsync(ApplicationDbContext db,int logId,string owner,string recipient,string kind,int hours,string reason,CancellationToken ct)
 {
  if(hours is <1 or >168||string.IsNullOrWhiteSpace(reason)||reason.Trim().Length is <10 or >1000)throw new ArgumentException("Choose 1–168 hours and provide a reason of 10–1000 characters.");
  var attemptId=await db.ProctoringLogs.Where(l=>l.LogId==logId&&l.Attempt!.Exam!.CreatedBy==owner&&l.ExamId==l.Attempt.ExamId&&l.StudentId==l.Attempt.StudentId).Select(l=>l.AttemptId).SingleOrDefaultAsync(ct)??throw new UnauthorizedAccessException();
  return await ExamResultFinalizationService.WriteAsync(db,attemptId,async()=>{
   var log=await db.ProctoringLogs.Include(l=>l.Attempt)!.ThenInclude(a=>a!.Exam).SingleAsync(l=>l.LogId==logId,ct);
   if(log.Attempt!.Exam!.CreatedBy!=owner||log.AlertType==ProctoringAlertType.SystemAudit||
      log.StudentId!=log.Attempt.StudentId||log.ExamId!=log.Attempt.ExamId)throw new UnauthorizedAccessException();
   if(!await db.UserRoles.AnyAsync(ur=>ur.UserId==recipient&&db.Roles.Any(r=>r.Id==ur.RoleId&&(r.Name=="Student"||r.Name=="Admin")),ct))throw new ArgumentException("Choose an existing Student or Administrator account.");
   var key=Key(log,kind);if(string.IsNullOrWhiteSpace(key))throw new ArgumentException("The selected evidence source is unavailable.");
   var now=DateTime.UtcNow;
   var prior=await db.EvidenceGrants.SingleOrDefaultAsync(g=>g.LogId==logId&&g.Kind==kind&&g.StorageKey==key&&g.RecipientId==recipient&&g.RevokedAtUtc==null&&g.ExpiresAtUtc>now,ct);
   if(prior!=null)return prior.Id;
   var grant=new EvidenceGrant{LogId=logId,OwnerId=owner,RecipientId=recipient,Kind=kind,StorageKey=key,Reason=reason.Trim(),CreatedAtUtc=now,ExpiresAtUtc=now.AddHours(hours)};
   db.EvidenceGrants.Add(grant);db.ProctoringLogs.Add(Audit(log,owner,"Granted",grant.Id,reason));return grant.Id;
  },ct);
 }
 public static async Task RevokeAsync(ApplicationDbContext db,Guid id,string owner,CancellationToken ct)
 {
  var attemptId=await db.EvidenceGrants.Where(g=>g.Id==id&&g.OwnerId==owner&&g.Log!.Attempt!.Exam!.CreatedBy==owner).Select(g=>g.Log!.AttemptId).SingleOrDefaultAsync(ct)??throw new UnauthorizedAccessException();
  await ExamResultFinalizationService.WriteAsync(db,attemptId,async()=>{
   var g=await db.EvidenceGrants.Include(g=>g.Log).ThenInclude(l=>l!.Attempt).ThenInclude(a=>a!.Exam).SingleAsync(g=>g.Id==id,ct);
   if(g.OwnerId!=owner||g.Log!.Attempt!.Exam!.CreatedBy!=owner)throw new UnauthorizedAccessException();
   if(g.RevokedAtUtc==null){g.RevokedAtUtc=DateTime.UtcNow;db.ProctoringLogs.Add(Audit(g.Log,owner,"Revoked",g.Id,"Owner revoked recipient access."));}return true;
  },ct);
 }
 public static IQueryable<EvidenceGrant> Available(ApplicationDbContext db,string recipient)=>db.EvidenceGrants.Where(g=>g.RecipientId==recipient&&g.RevokedAtUtc==null&&g.ExpiresAtUtc>DateTime.UtcNow&&g.Log!.Attempt!.Exam!.CreatedBy==g.OwnerId&&g.Log.ExamId==g.Log.Attempt.ExamId&&g.Log.StudentId==g.Log.Attempt.StudentId);
 public static ProctoringLog Audit(ProctoringLog log,string actor,string action,Guid grantId,string reason)=>new(){AttemptId=log.AttemptId,ExamId=log.ExamId,StudentId=log.StudentId,ParentIncidentId=log.LogId,AlertType=ProctoringAlertType.SystemAudit,CountsAsViolation=false,IsReviewed=true,ModelName="Evidence sharing",ActionByUserId=actor,Description=action+" selected evidence",MachineObservationsJson=JsonSerializer.Serialize(new{action,grantId,actor,reason,atUtc=DateTime.UtcNow})};
 public static void Redact(ProctoringLog log){log.ScreenshotPath=null;log.RecordingPath=null;log.VideoEvidencePath=null;log.SpeechTranscript=null;log.MachineObservationsJson=null;log.Description=log.AlertTypeDisplay;}
}
