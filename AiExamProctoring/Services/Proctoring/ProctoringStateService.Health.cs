using System.Data;
using System.Text.Json;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services.Proctoring;

public sealed partial class ProctoringStateService
{
    public async Task<DeviceHealthReply?> HealthAsync(int attemptId, int sessionId, string studentId,
        string issue, CancellationToken ct = default)
    {
        if (!DeviceHealthPolicy.IsIssue(issue)) return null;
        DeviceHealthReply? reply = null;
        string? owner = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            reply = null;
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await LockAttemptAsync(db, attemptId, ct);
            var s = await db.ExamSessionAttempts.Include(x => x.Session)
                .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
                .SingleOrDefaultAsync(x => x.SessionAttemptId == sessionId &&
                    x.AttemptId == attemptId && x.Attempt!.StudentId == studentId, ct);
            var a = s?.Attempt;
            if (a is null || s is null || a.Status != AttemptStatus.InProgress ||
                s.Status != AttemptStatus.InProgress || s.FrozenAtUtc.HasValue) return;

            DeviceHealthEpisode? prior = null;
            try { if (s.DeviceHealthJson is not null) prior = JsonSerializer.Deserialize<DeviceHealthEpisode>(s.DeviceHealthJson); }
            catch (JsonException) { /* Restart invalid legacy telemetry, do not count as cheating. */ }
            var now = DateTime.UtcNow;
            var decision = DeviceHealthPolicy.Sample(prior, issue, now, settings.DeviceHoldConfirmationSeconds);
            var episode = decision.Episode;
            ProctoringLog? audit = null;
            var recovered = prior is { Issue: not "healthy" } && episode?.Issue == "healthy";
            var newFault = episode is { Issue: not "healthy" } &&
                (prior is null || prior.Issue == "healthy" || prior.Started != episode.Started);
            if (recovered || (newFault && !decision.Hold))
            {
                var kind = DeviceHealthPolicy.CameraAffected(issue) ? ProctoringAlertType.CameraMonitoringInterrupted
                    : DeviceHealthPolicy.MicrophoneAffected(issue) ? ProctoringAlertType.MicrophoneAccessLost
                    : ProctoringAlertType.AiMonitoringUnavailable;
                var description = issue == "healthy"
                    ? "[DEVICE_RECOVERED] Camera and microphone observations have recovered; ongoing readiness is monitored."
                    : $"[DEVICE_WARNING] Technical problem: {issue.Replace('_', ' ')}. Check device connection and permissions.";
                audit = Audit(a, s, kind, description);
                if (issue != "healthy")
                {
                    audit.IsReviewed = false; // Appears in Teacher live alert list.
                    audit.Severity = ProctoringSeverity.Medium;
                }
                db.ProctoringLogs.Add(audit);
                a.StateVersion++;
            }
            if (decision.Hold && episode is not null)
            {
                episode = episode with { IsTechnicalHold = true };
                // FreezeAsync persists the remaining seconds and saved answers under
                // the same locked SQL transaction. It does NOT increment strikes.
                await FreezeAsync(a, s,
                    $"[DEVICE_HOLD] {issue}: monitoring failed for at least {Math.Clamp(settings.DeviceHoldConfirmationSeconds, 5, 60):0.#} seconds. " +
                    "Answers and remaining time are saved; pass fresh camera/microphone checks before resuming. This is not cheating.", ct);
                a.StateVersion++;
                audit = Audit(a, s, ProctoringAlertType.AiMonitoringUnavailable,
                    $"[DEVICE_HOLD] Technical monitoring pause: {issue}. Camera affected: {episode.CameraAffected}; microphone affected: {episode.MicrophoneAffected}. No cheating strike.");
                audit.IsReviewed = false;
                audit.Severity = ProctoringSeverity.High; // Urgent technical attention, NOT misconduct.
                db.ProctoringLogs.Add(audit);
                db.StudentNotifications.Add(new StudentNotification
                {
                    StudentId = a.StudentId,
                    ExamId = a.ExamId,
                    Title = "Exam paused — technical device problem",
                    Message = $"Problem: {issue}. Your answers and remaining time are saved. Reconnect the required device(s), then complete the fresh readiness checks to resume. No cheating strike was added.",
                    Type = "DeviceTechnicalHold",
                    CreatedDate = DateTime.Now,
                    IsRead = false
                });
            }
            s.DeviceHealthJson = episode is null ? null : JsonSerializer.Serialize(episode);
            await db.SaveChangesAsync(ct);
            owner = a.Exam?.CreatedBy;
            reply = new DeviceHealthReply(decision.Warn || decision.Hold, episode?.Issue ?? issue,
                episode?.Warnings ?? 0, decision.Hold, State(a, s, audit));
            await tx.CommitAsync(ct);
        });
        if (reply is not null && owner is not null && (reply.State.EventId.HasValue || reply.NeedsReview))
            await PublishAsync(reply.State, owner,
                reply.NeedsReview ? "ExamLocked" : reply.Issue == "healthy" ? "DeviceRecovered" : "DeviceWarning");
        return reply;
    }

    public async Task<DeviceRecoveryReply> RecoverDeviceAsync(int attemptId, int sessionId,
        string studentId, DeviceReadinessSample sample, CancellationToken ct = default)
    {
        DeviceRecoveryReply result = new(false, false, 0,
            "This examination is not in a recoverable technical hold.", null);
        string? owner = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            result = new(false, false, 0, "This examination is not in a recoverable technical hold.", null);
            owner = null;
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await LockAttemptAsync(db, attemptId, ct);
            var s = await db.ExamSessionAttempts.Include(x => x.Session)
                .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
                .SingleOrDefaultAsync(x => x.SessionAttemptId == sessionId && x.AttemptId == attemptId &&
                    x.Attempt!.StudentId == studentId, ct);
            var a = s?.Attempt;
            if (a is null || s is null || a.Status != AttemptStatus.ProctoringBlocked ||
                s.Status != AttemptStatus.ProctoringBlocked || !s.FrozenAtUtc.HasValue) return;
            DeviceHealthEpisode? prior = null;
            try { if (s.DeviceHealthJson is not null) prior = JsonSerializer.Deserialize<DeviceHealthEpisode>(s.DeviceHealthJson); }
            catch (JsonException) { return; }
            if (prior?.IsTechnicalHold != true) return; // Never release a teacher/cheating lock.
            var holdRecord = await db.ProctoringLogs.AsNoTracking()
                .Where(l => l.AttemptId == attemptId && l.AlertType == ProctoringAlertType.ExamBlocked)
                .OrderByDescending(l => l.LogId).Select(l => l.Description).FirstOrDefaultAsync(ct);
            if (holdRecord?.StartsWith("[DEVICE_HOLD]", StringComparison.Ordinal) != true) return;
            var now = DateTime.UtcNow;
            var decision = DeviceHealthPolicy.Recover(prior, sample, now,
                settings.DeviceRecoveryConfirmationSeconds, settings.DeviceRecoverySamples);
            s.DeviceHealthJson = JsonSerializer.Serialize(decision.Episode);
            if (!decision.Recovered)
            {
                await db.SaveChangesAsync(ct);
                result = new(true, false, decision.Episode.RecoverySamples, decision.Message, State(a, s));
                await tx.CommitAsync(ct);
                return;
            }

            if (s.Session is null) return;
            var remaining = Math.Max(0, s.RemainingTimeSeconds ?? 0);
            if (remaining == 0)
            {
                result = new(true, false, decision.Episode.RecoverySamples,
                    "The saved session has no time remaining; ask your instructor for review.", State(a, s));
                await tx.CommitAsync(ct);
                return;
            }
            // Same timer restoration calculation as existing authorized Teacher
            // resumption: elapsed hold time does NOT consume exam minutes.
            s.StartedAt = DateTime.Now.AddSeconds(remaining).AddMinutes(-s.Session.DurationMinutes);
            s.FrozenAtUtc = null;
            s.RemainingTimeSeconds = null;
            s.ProctoringIncidentsJson = null;
            s.ObjectTrackingJson = null;
            s.DeviceHealthJson = JsonSerializer.Serialize(new DeviceHealthEpisode(
                "healthy", now, now, 0, ClientSeen: now));
            a.Status = s.Status = AttemptStatus.InProgress;
            a.TeacherResumedAtUtc = now; // Reject any late pre-recovery inference.
            a.StateVersion++;
            var log = Audit(a, s, ProctoringAlertType.ExamUnblocked,
                $"[DEVICE_RECOVERED] Fresh device readiness passed: camera={prior.CameraAffected}; microphone={prior.MicrophoneAffected}. " +
                $"Restored {remaining} seconds without changing existing cheating strikes.");
            db.ProctoringLogs.Add(log);
            db.StudentNotifications.Add(new StudentNotification
            {
                StudentId = a.StudentId,
                ExamId = a.ExamId,
                Title = "Technical device checks passed",
                Message = "Your technical pause has ended. Your saved answers and remaining exam time are retained. A fresh entry identity check may still be required.",
                Type = "DeviceRecovered",
                CreatedDate = DateTime.Now,
                IsRead = false
            });
            await db.SaveChangesAsync(ct);
            result = new(true, true, decision.Episode.RecoverySamples,
                "Technical hold released; continue with the normal exam entry/identity check.", State(a, s, log));
            owner = a.Exam?.CreatedBy;
            await tx.CommitAsync(ct);
        });
        if (result.Recovered && result.State is not null && owner is not null)
            await PublishAsync(result.State, owner, "ResumeExamSignal");
        return result;
    }
}
