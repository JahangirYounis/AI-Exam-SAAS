using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace AiExamProctoring.Areas.Student.Controllers;
public partial class ExamController {
 [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(30000)]
 [Route("/Student/Exam/BrowserObservation")]
 public async Task<IActionResult> BrowserObservation(int attemptId,int sessionAttemptId,string eventId,string kind,string phase,double elapsed,string surface,CancellationToken ct,DateTimeOffset? observedAt=null,DateTimeOffset? startedAt=null) {
  if(StudentId is null)return Unauthorized();
  if(!Guid.TryParse(eventId,out var id)||!double.IsFinite(elapsed)||elapsed<0||elapsed>86400||phase is not ("start" or "update" or "end" or "stop"))return BadRequest();
  var type=kind switch {"ExamPageHidden"=>ProctoringAlertType.ExamPageHidden,"ExamWindowLeft"=>ProctoringAlertType.ExamWindowLeft,
   "FullScreenExit"=>ProctoringAlertType.FullScreenExit,"ScreenShareLost"=>ProctoringAlertType.ScreenShareLost,
   "ScreenCaptureUnavailable"=>ProctoringAlertType.ScreenCaptureUnavailable,_=>(ProctoringAlertType?)null};
  if(type is null || (phase=="stop" && type!=ProctoringAlertType.FullScreenExit))return BadRequest();
  // Native browser reports cannot assert Tab Switching.
  var fullscreen=type==ProctoringAlertType.FullScreenExit;
  surface=surface is "monitor" or "window" or "browser"?surface:"unknown";
  var key="browser:"+id.ToString("N");
  var now=DateTime.UtcNow;
  var notify=false;
  var apply=false;
  var transactionResult = await _context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult?>(async () => {
  notify=false;
  apply=false;
  _context.ChangeTracker.Clear();
  await using(var tx=await _context.Database.BeginTransactionAsync(ct)) {
   await ProctoringStateService.LockAttemptAsync(_context,attemptId,ct);
   var session=await _context.ExamSessionAttempts.Include(s=>s.Attempt).ThenInclude(a=>a!.Exam).SingleOrDefaultAsync(s=>s.SessionAttemptId==sessionAttemptId&&s.AttemptId==attemptId&&s.Attempt!.StudentId==StudentId,ct);
   if(session?.Attempt is null)return NotFound();
   var log=await _context.ProctoringLogs.SingleOrDefaultAsync(l=>l.ClientEventId==key,ct);
   if(log is not null && (log.StudentId!=StudentId||log.AttemptId!=attemptId||log.SessionAttemptId!=sessionAttemptId||log.AlertType!=type))return Conflict();
   if(log is null){
    if(fullscreen && (phase=="stop" || elapsed<1.5 || session.Attempt.Exam?.RequireFullscreen!=true ||
      session.Status!=AttemptStatus.InProgress || session.Attempt.Status!=AttemptStatus.InProgress))return Json(new {ok=true,acknowledged=true});
    notify=true;
    log=new ProctoringLog {StudentId=StudentId,ExamId=session.Attempt.ExamId,AttemptId=attemptId,SessionAttemptId=sessionAttemptId,
     ClientEventId=key,AlertType=type.Value,Severity=ProctoringSeverity.Low,IncidentKey="browser",CountsAsViolation=false,PolicyProcessed=type!=ProctoringAlertType.FullScreenExit,
     CreatedDate=now,FrameCapturedAt=now,IncidentStartedAtUtc=now,ModelName="Browser visibility signals",ModelVersion="browser-signals-v2"};
    _context.ProctoringLogs.Add(log);
   }
   if(log.IncidentEndedAtUtc is null){
    var captureStart=SessionRecordingMetadata.BrowserMarker(log.MachineObservationsJson);
    if(captureStart is null && startedAt<=now.AddMinutes(2) &&
      startedAt>=session.Attempt.StartedAt.ToUniversalTime().AddMinutes(-2) && startedAt<=observedAt)
     captureStart=startedAt;
    if(fullscreen && captureStart is not null) log.IncidentStartedAtUtc=captureStart.Value.UtcDateTime;
    log.ObservedDurationSeconds=Math.Max(log.ObservedDurationSeconds??0,elapsed);
    log.Description=type==ProctoringAlertType.ExamPageHidden?$"The exam page was hidden for {log.ObservedDurationSeconds:0.0} seconds. The specific action is unknown and requires review.":
      type==ProctoringAlertType.ExamWindowLeft?$"The exam window lost focus for {log.ObservedDurationSeconds:0.0} seconds. The destination is unknown and requires review.":
      fullscreen ? phase=="end" ? "Fullscreen exited and restored." : phase=="stop" ?
        "Fullscreen exited. Monitoring ended before fullscreen was restored." : "Fullscreen exited. Select Restore fullscreen to return." :
        "Screen monitoring was interrupted or unavailable. This is a technical observation requiring review.";
    log.RecoveryStatus=type==ProctoringAlertType.ScreenCaptureUnavailable?"Unavailable":
      phase=="end"?"Returned":phase=="stop"?"MonitoringEnded":"UnderObservation";
    if(phase is "end" or "stop"){log.IncidentEndedAtUtc=now;notify=true;}
    if(fullscreen && !log.PolicyProcessed && log.ObservedDurationSeconds>=1.5 &&
      session.Status==AttemptStatus.InProgress && session.Attempt.Status==AttemptStatus.InProgress &&
      session.Attempt.Exam?.RequireFullscreen==true)
     log.PolicyPending=true;
    var observation=JsonSerializer.Serialize(new {surface,elapsedSource="client_monotonic_report",serverReceivedAt=now,
      clientObservedAt=observedAt,clientStartedAt=captureStart,externalUrlObservable=false,
      scope=surface=="monitor"?"Visible pixels on selected display only":surface=="window"?"Selected window only":surface=="browser"?"Selected tab only":"Capture scope unavailable"});
    log.MachineObservationsJson=SessionRecordingMetadata.UpdateBrowserMetadata(log.MachineObservationsJson, observation);
   }
   apply=log.PolicyPending&&!log.PolicyProcessed;
   await _context.SaveChangesAsync(ct);await tx.CommitAsync(ct);

  }
  return null;
  });
  if(transactionResult is not null)return transactionResult;
  var saved=await _context.ProctoringLogs.AsNoTracking().SingleAsync(l=>l.ClientEventId==key,ct);
  if(apply)await _states.ApplyLogAsync(saved.LogId,ct);
  else if(notify)await _states.NotifyObservationAsync(saved.LogId,ct);
  return Json(new {ok=true,acknowledged=true,logId=saved.LogId});
 }
}
