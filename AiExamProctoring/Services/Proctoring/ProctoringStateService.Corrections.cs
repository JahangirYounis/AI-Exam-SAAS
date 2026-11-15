using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace AiExamProctoring.Services.Proctoring;
public sealed partial class ProctoringStateService {
 public async Task<bool> CorrectIncidentAsync(int logId,string actor,string reason,CancellationToken ct=default){
  if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length is < 10 or > 300)return false;
  var attempt=await db.ProctoringLogs.Where(l=>l.LogId==logId).Select(l=>l.AttemptId).SingleOrDefaultAsync(ct);if(attempt is null)return false;
  return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
  await using var tx=db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;await LockAttemptAsync(db,attempt.Value,ct);
  var log=await db.ProctoringLogs.SingleAsync(l=>l.LogId==logId,ct);
  await db.Entry(log).ReloadAsync(ct);
  var a=await db.ExamAttempts.Include(a=>a.Exam).SingleAsync(a=>a.AttemptId==attempt,ct);
  await db.Entry(a).ReloadAsync(ct);
  var admin=await (from u in db.UserRoles join r in db.Roles on u.RoleId equals r.Id where u.UserId==actor&&r.Name=="Admin" select u).AnyAsync(ct);
  if(!admin && a.Exam!.CreatedBy!=actor && !await db.ExamInvigilators.AnyAsync(i=>i.ExamId==a.ExamId&&i.TeacherId==actor&&i.IsActive&&i.CanMonitor&&i.AcceptedAtUtc!=null && i.RevokedAtUtc == null,ct))return false;
  var key="policy-correction:"+logId;
  if(await db.ProctoringLogs.AnyAsync(l=>l.ClientEventId==key,ct)||!log.CountsAsViolation)return true;
  var before=a.StrikeCount;log.CountsAsViolation=false;
  if(IsStandard(log.AlertType) && (!a.Exam!.ResetWarningsOnResume || a.TeacherResumedAtUtc is null || (log.PolicyAppliedAt??log.CreatedDate).ToUniversalTime()>a.TeacherResumedAtUtc))a.StrikeCount=Math.Max(0,a.StrikeCount-1);
  a.TotalViolations=Math.Max(0,a.TotalViolations-1);a.StateVersion++;
  db.ProctoringLogs.Add(new(){StudentId=a.StudentId,ExamId=a.ExamId,AttemptId=a.AttemptId,SessionAttemptId=log.SessionAttemptId,ParentIncidentId=logId,ClientEventId=key,AlertType=ProctoringAlertType.ReviewRequested,ModelName="Policy correction",Description="Counted incident excluded after authorized review. "+reason,CountsAsViolation=false,PolicyProcessed=true,ActionByUserId=actor,CreatedDate=DateTime.Now,MachineObservationsJson=JsonSerializer.Serialize(new{originalIncidentId=logId,previousCount=before,currentCount=a.StrikeCount,policyVersion=a.Exam!.BlockingPolicyVersion??"legacy-v1"})});
  await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);
  var state=tx is not null ? await GetAsync(a.AttemptId,ct) : null;if(state is not null && tx is not null)await PublishAsync(state,a.Exam.CreatedBy??actor,"CandidateStateChanged");return true;
  });
 }
}
