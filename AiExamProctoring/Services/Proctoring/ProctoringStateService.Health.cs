using System.Data;
using System.Text.Json;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Services.Proctoring;
public sealed partial class ProctoringStateService {
 public async Task<DeviceHealthReply?> HealthAsync(int attemptId,int sessionId,string studentId,string issue,CancellationToken ct=default) {
  DeviceHealthReply? reply=null; string owner="";
  await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
   reply=null; db.ChangeTracker.Clear();
   await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
   await LockAttemptAsync(db,attemptId,ct);
   var s=await db.ExamSessionAttempts.Include(x=>x.Session).Include(x=>x.Attempt).ThenInclude(a=>a!.Exam).SingleOrDefaultAsync(x=>x.SessionAttemptId==sessionId&&x.AttemptId==attemptId&&x.Attempt!.StudentId==studentId,ct);
   var a=s?.Attempt;
   if(a==null||s==null||a.Status!=AttemptStatus.InProgress||s.FrozenAtUtc.HasValue)return;
   DeviceHealthEpisode? prior=null;
   try {if(s.DeviceHealthJson!=null)prior=JsonSerializer.Deserialize<DeviceHealthEpisode>(s.DeviceHealthJson);}catch(JsonException){}
   var decision=DeviceHealthPolicy.Sample(prior,issue,DateTime.UtcNow);
   s.DeviceHealthJson=decision.Episode is null?null:JsonSerializer.Serialize(decision.Episode);
   ProctoringLog? log=null;
   if(decision.Warn || prior!=null&&decision.Episode==null) {
    log=Audit(a,s,ProctoringAlertType.AiMonitoringUnavailable,decision.Episode==null?"[DEVICE_RECOVERED] Camera and microphone checks recovered.":$"[DEVICE_WARNING] Technical issue: {issue}. Warning {decision.Episode.Warnings}/3. Please check your device and lighting. This is not a cheating strike.");
    db.ProctoringLogs.Add(log); a.StateVersion++;
   }
   if(decision.Hold && BlockingPolicy.ShouldAutoLock(a.Exam!.BlockingMode,0,true)) {
    log=Audit(a,s,ProctoringAlertType.AiMonitoringUnavailable,"[DEVICE_HOLD] Monitoring unavailable for 20 seconds. Technical review required, not a cheating finding."); db.ProctoringLogs.Add(log);
    await FreezeAsync(a,s,"[DEVICE_HOLD] Monitoring unavailable for 20 seconds. Technical review required, not a cheating finding.",ct); a.StateVersion++;
   }
   await db.SaveChangesAsync(ct);
   owner=a.Exam!.CreatedBy!;
   reply=new(decision.Warn,issue,decision.Episode?.Warnings??0,decision.Hold,State(a,s,log));
   await tx.CommitAsync(ct);
  });
  if(reply!=null&&(reply.Warning||reply.State.Status==nameof(AttemptStatus.ProctoringBlocked))) await PublishAsync(reply.State,owner,reply.State.Status==nameof(AttemptStatus.ProctoringBlocked)?"ExamLocked":"DeviceWarning");
  return reply;
 }
}
