using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Student.Controllers;
public partial class ExamController {
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16000)]
    public async Task<IActionResult> MonitoringGap(int attemptId, int sessionAttemptId, string eventId, string reason, double durationSeconds, CancellationToken ct) {
        if (StudentId is null) return Unauthorized();
        if (!Guid.TryParse(eventId, out var id) || !double.IsFinite(durationSeconds) || durationSeconds <= 0 || durationSeconds > 60 ||
            reason is not ("backpressure" or "connection_lost" or "reconnecting" or "warning_playback")) return BadRequest();
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () => {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            await ProctoringStateService.LockAttemptAsync(_context, attemptId, ct);
            var session = await _context.ExamSessionAttempts.Include(s => s.Attempt).SingleOrDefaultAsync(s =>
                s.SessionAttemptId == sessionAttemptId && s.AttemptId == attemptId && s.Attempt!.StudentId == StudentId, ct);
            if (session?.Attempt is null) return NotFound();
            var key = "audio-gap:" + id.ToString("N");
            var prior = await _context.ProctoringLogs.SingleOrDefaultAsync(l => l.ClientEventId == key, ct);
            if (prior is not null) return prior.AttemptId == attemptId && prior.SessionAttemptId == sessionAttemptId && prior.StudentId == StudentId
                ? Json(new { acknowledged = true }) : Conflict();
            var now=DateTime.UtcNow;
            var parent=await _context.ProctoringLogs.Where(l=>l.AttemptId==attemptId && l.SessionAttemptId==sessionAttemptId &&
                l.ParentIncidentId==null && l.ModelName=="Audio monitoring continuity" && l.ModelVersion==reason &&
                l.IncidentEndedAtUtc>=now.AddSeconds(-15)).OrderByDescending(l=>l.LogId).FirstOrDefaultAsync(ct);
            if(parent is not null){
                parent.IncidentEndedAtUtc=now;
                parent.ObservedDurationSeconds=(parent.ObservedDurationSeconds??0)+durationSeconds;
            }
            _context.ProctoringLogs.Add(new ProctoringLog {
                AttemptId=attemptId, SessionAttemptId=sessionAttemptId, StudentId=StudentId, ExamId=session.Attempt.ExamId,
                ClientEventId=key, ParentIncidentId=parent?.LogId,
                AlertType=parent is not null || reason=="warning_playback" ? ProctoringAlertType.SystemAudit : ProctoringAlertType.AiMonitoringUnavailable,
                CountsAsViolation=false, PolicyProcessed=true, ModelVersion=reason,
                IncidentStartedAtUtc=now.AddSeconds(-durationSeconds),IncidentEndedAtUtc=now,
                CreatedDate=now, ModelName="Audio monitoring continuity", ObservedDurationSeconds=durationSeconds,
                Description="An audio monitoring interval was not analyzed. This is a technical observation, not a rule violation.",
                MachineObservationsJson=System.Text.Json.JsonSerializer.Serialize(new { reason, durationSeconds, source="client_report", serverReceivedAt=DateTime.UtcNow }) });
            await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Json(new { acknowledged = true });
        });
    }
}
