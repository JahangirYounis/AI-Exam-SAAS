
using System.Security.Claims;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher"), Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlertsController(ApplicationDbContext db, IProctoringStateService? states = null) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    // Reuse the existing account-linked notification table for per-recipient receipts.
    // Its historical StudentId column references ApplicationUser, including teachers.
    private const string ReadPrefix = "TeacherIncidentRead:";
    // An incident must belong to an actual Student-role ExamAttempt in an
    // owner/accepted CanMonitor exam. ProctoringLog.StudentId alone is not proof.
    private IQueryable<ProctoringLog> Authorized() => TeacherEvidenceAccess.Incidents(db, UserId);
    private IQueryable<ProctoringLog> Inbox() => Authorized().Where(l =>
        TeacherAlertContract.MonitoringKinds.Contains(l.AlertType) ||
        l.AlertType == ProctoringAlertType.ExamBlocked || l.AlertType == ProctoringAlertType.ExamUnblocked ||
        l.AlertType == ProctoringAlertType.ReviewRequested).Where(l =>
        l.AlertType == ProctoringAlertType.FaceNotVisible ||
        (l.IncidentKey == "attention" && l.ParentIncidentId == null && l.PolicyProcessed &&
            (l.AlertType == ProctoringAlertType.HeadAway || l.AlertType == ProctoringAlertType.GazeAway ||
                l.AlertType == ProctoringAlertType.LookAway)) ||
        l.AlertType == ProctoringAlertType.IdentityMismatch && l.Severity == ProctoringSeverity.High ||
        l.AlertType == ProctoringAlertType.ObjectObservation ||
        (l.AlertType == ProctoringAlertType.ElectronicDeviceDetected && l.PolicyProcessed &&
            l.IncidentKey != null && l.IncidentKey.StartsWith("object:")) ||
        // Fast audio observations appear before transcription; same LogId is later
        // enriched in place, so the teacher gets one durable notification.
        (l.AlertType == ProctoringAlertType.AudioDetected &&
            (l.ModelName == "Speech screening" || l.ModelName == "Ambient audio screening")) ||
        (l.ModelName == "Speech screening" && l.IncidentKey == "speech" && l.ParentIncidentId == null &&
            (l.AlertType == ProctoringAlertType.TalkingDetected || l.AlertType == ProctoringAlertType.AnswerPromptSpeech)) ||
        (l.ModelName == "Speech screening" && l.SpeechTranscript != null &&
            !db.ProctoringLogs.Any(other => other.SessionAttemptId == l.SessionAttemptId && other.ClientEventId == "speech-context:" + l.ModelVersion) &&
            !db.ProctoringLogs.Any(other => other.SessionAttemptId == l.SessionAttemptId && other.ModelName == "Speech screening" && other.ModelVersion == l.ModelVersion && other.SpeechTranscript != null && other.LogId < l.LogId)) || l.IncidentKey == "browser" || l.AlertType == ProctoringAlertType.ExamPageHidden || l.AlertType == ProctoringAlertType.AdditionalPersonObservation || l.CountsAsViolation && l.PolicyProcessed || l.AlertType == ProctoringAlertType.AiMonitoringUnavailable ||
        l.AlertType == ProctoringAlertType.CameraMonitoringInterrupted || l.AlertType == ProctoringAlertType.CameraAccessLost || l.AlertType == ProctoringAlertType.MicrophoneAccessLost ||
        l.AlertType == ProctoringAlertType.ExamBlocked || l.AlertType == ProctoringAlertType.ExamUnblocked || l.AlertType == ProctoringAlertType.ReviewRequested);

    [HttpGet]
    public async Task<IActionResult> Feed(int before = 0, CancellationToken cancellationToken = default, bool unreadOnly = false)
    {
        var query = Inbox().AsNoTracking();
        var unread = await query.CountAsync(l => !db.StudentNotifications.Any(n => n.StudentId == UserId && n.Type == ReadPrefix + l.LogId), cancellationToken);
        if (unreadOnly) query = query.Where(l => !db.StudentNotifications.Any(n => n.StudentId == UserId && n.Type == ReadPrefix + l.LogId)); if (before > 0) query = query.Where(l => l.LogId < before);
        var logs = await query.Include(l => l.Student).Include(l => l.Exam).Include(l => l.Attempt)
            .OrderByDescending(l => l.LogId).Take(50).ToListAsync(cancellationToken);
        var evidenceIds = logs.Select(EvidenceIncidentId).Distinct().ToArray();
        var evidence = Authorized().AsNoTracking().Where(l =>
            l.ScreenshotPath != null || l.VideoEvidencePath != null || l.RecordingPath != null);
        var withEvidence = (await evidence.Where(l => evidenceIds.Contains(l.LogId))
            .Select(l => l.LogId).Union(evidence.Where(l => l.ParentIncidentId.HasValue &&
                evidenceIds.Contains(l.ParentIncidentId.Value)).Select(l => l.ParentIncidentId!.Value))
            .ToListAsync(cancellationToken)).ToHashSet();
        var types = logs.Select(l => ReadPrefix + l.LogId).ToArray();
        var receipts = await db.StudentNotifications.Where(n => n.StudentId == UserId && types.Contains(n.Type)).Select(n => n.Type).ToListAsync(cancellationToken);
        return Json(new
        {
            version = 1,
            unread,
            nextBefore = logs.Count == 50 ? logs[^1].LogId : (int?)null,
            items = logs.Select(l => new {
                id = l.LogId,
                attemptId = l.AttemptId,
                student = l.Student?.FullName ?? "Candidate",
                exam = l.Exam!.ExamTitle,
                title = l.IncidentKey == "absence:escalation" ? l.Description ?? "Student still absent" :
                    l.AlertType == ProctoringAlertType.ExamBlocked ? "Student blocked" :
                    l.AlertType == ProctoringAlertType.AnswerPromptSpeech ? "Exam-Related Conversation — Review" :
                    l.ModelName == "Ambient audio screening" ? "Background Sound — Manual Review" :
                    l.ModelName == "Speech screening" ? l.AlertType == ProctoringAlertType.TalkingDetected ?
                       l.CountsAsViolation ? "Repeated Speech Warning" : l.Description ?? "Speech Notice"
                       : "Speech Heard — Speaker Under Review" :
                    TeacherAlertContract.Title(l),
                category = TeacherAlertContract.Category(l),
                warningNumber = l.WarningNumber,
                allowedWarnings = l.Exam.AllowedWarnings,
                policyVersion = l.Exam.BlockingPolicyVersion ?? "legacy-v1",
                priority = l.AlertType == ProctoringAlertType.IdentityMismatch ? "Urgent" : l.Severity.ToString(),
                maximumSimultaneousPeople = l.MaxSimultaneousPeople,
                incidentStartedAt = l.IncidentStartedAtUtc,
                incidentEndedAt = l.IncidentEndedAtUtc,
                durationSeconds = l.ObservedDurationSeconds,
                recoveryStatus = l.RecoveryStatus,
                occurredAt = l.CreatedDate,
                read = receipts.Contains(ReadPrefix + l.LogId),
                reviewed = l.IsReviewed,
                description = l.AlertType == ProctoringAlertType.ObjectObservation ? $"A possible {l.ModelName?.Replace(" Detected", "").ToLowerInvariant()} was detected near {l.Student?.FullName ?? "Candidate"}. Review the evidence." : l.AlertType == ProctoringAlertType.ExamBlocked ? l.ActionByUserId != null ? $"Exam blocked manually by {l.ActionByName ?? l.ActionByUserId} at {l.CreatedDate:u}." : l.Description
                    : l.IncidentKey == "absence:escalation" ? l.Description
                    : l.AlertType == ProctoringAlertType.FaceNotDetected ? $"{l.Student?.FullName ?? "Candidate"} was absent from the camera view for {(l.ObservedDurationSeconds.HasValue ? l.ObservedDurationSeconds.Value.ToString("0.0") : l.DurationSeconds.ToString())} seconds."
                    : l.AlertType == ProctoringAlertType.MultipleFaces ? $"{l.MaxSimultaneousPeople?.ToString() ?? "Multiple"} possible live people were detected in {l.Student?.FullName ?? "Candidate"}'s examination area."
                    : l.IncidentKey == "attention" ? $"{l.Student?.FullName ?? "Candidate"} appeared to look away from the exam screen for {(l.ObservedDurationSeconds.HasValue ? l.ObservedDurationSeconds.Value.ToString("0.0") : l.DurationSeconds.ToString())} seconds."
                    : l.WarningNumber.HasValue && l.CountsAsViolation ? l.WarningNumber == l.Exam.AllowedWarnings && l.Exam.BlockingMode != ExamBlockingMode.MANUAL_ONLY ? $"Candidate {l.Student?.FullName ?? "Candidate"} has received the final warning for {TeacherAlertContract.Title(l.AlertType)}. Another qualifying violation will automatically block the exam." : $"Candidate {l.Student?.FullName ?? "Candidate"} received Warning {l.WarningNumber} for {TeacherAlertContract.Title(l.AlertType)}."
                    : l.AlertType == ProctoringAlertType.IdentityMismatch
                    ? $"URGENT: The person taking Candidate {l.Student?.FullName ?? "Candidate"}'s examination may not match the registered candidate. Repeated identity checks require immediate review."
                    : l.AlertType == ProctoringAlertType.TalkingDetected ? l.Description ?? "Repeated intelligible speech was heard near the microphone. Speaker identity is unknown; review the recorded audio." : l.AlertType == ProctoringAlertType.AnswerPromptSpeech ? "Possible exam-related conversation was detected during the assessment. Recorded speech appears to reference questions, answers, or answer options. Review the evidence." : l.ModelName == "Speech screening" ? "Speech was observed. Speaker identity and context require review." : l.ModelName == "Ambient audio screening" ? l.Description : l.AlertType == ProctoringAlertType.ObjectObservation ? l.Description : l.IncidentKey == "browser" ? l.Description : l.AlertType is ProctoringAlertType.FaceNotDetected or ProctoringAlertType.MultipleFaces or ProctoringAlertType.AdditionalPersonObservation ? l.Description : "Review the recorded observation and available evidence. This is not a finding of cheating.",
                hasEvidence = withEvidence.Contains(EvidenceIncidentId(l)),
                url = Url.Action(nameof(Incident), new { id = l.LogId }),
                liveUrl = l.Attempt != null && l.Attempt.Status == AttemptStatus.InProgress
                    ? Url.Action("Live", "Monitoring", new { examId = l.ExamId, attemptId = l.AttemptId,
                        incidentId = EvidenceIncidentId(l) }) : null
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> Incident(int id, CancellationToken cancellationToken)
    {
        var log = await Authorized().AsNoTracking().SingleOrDefaultAsync(l => l.LogId == id, cancellationToken);
        if (log is null) return NotFound();
        var evidenceId = EvidenceIncidentId(log);
        if (evidenceId != id && !await Authorized().AnyAsync(l => l.LogId == evidenceId &&
            l.AttemptId == log.AttemptId && l.IncidentKey == "absence", cancellationToken))
            evidenceId = id;
        var fragment = log.AlertType is ProctoringAlertType.ExamPageHidden or ProctoringAlertType.TabSwitch or ProctoringAlertType.FullScreenExit
            ? "#session-timeline" : "#incident-" + evidenceId;
        return Redirect(Url.Action("ExamFolder", "Monitoring", new
        {
            examId = log.ExamId,
            studentId = log.StudentId,
            attemptId = log.AttemptId,
            incidentId = evidenceId
        }) + fragment);
    }

    private static int EvidenceIncidentId(ProctoringLog log) =>
        log.IncidentKey == "absence:escalation" ? log.ParentIncidentId ?? log.LogId : log.LogId;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAll(CancellationToken cancellationToken)
    {
        var ids = await Inbox().AsNoTracking().Where(l => !db.StudentNotifications.Any(n => n.StudentId == UserId && n.Type == ReadPrefix + l.LogId))
            .OrderBy(l => l.LogId).Select(l => l.LogId).ToListAsync(cancellationToken);
        foreach (var id in ids) await Read(id, cancellationToken);
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(int id, CancellationToken cancellationToken)
    {
        // Serialize receipt writes using the existing attempt lock; repeated requests are idempotent.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            if (db.Database.IsSqlServer()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id={UserId}", cancellationToken); var log = await Inbox().SingleOrDefaultAsync(l => l.LogId == id, cancellationToken);
            if (log is null) return NotFound();
            if (log.AttemptId is int attempt) await ProctoringStateService.LockAttemptAsync(db, attempt, cancellationToken);
            if (!await db.StudentNotifications.AnyAsync(n => n.StudentId == UserId && n.Type == ReadPrefix + id, cancellationToken))
                db.StudentNotifications.Add(new StudentNotification
                {
                    StudentId = UserId,
                    ExamId = log.ExamId,
                    Type = ReadPrefix + id,
                    Title = "Incident read receipt",
                    Message = "Incident " + id,
                    IsRead = true,
                    ReadDate = DateTime.UtcNow,
                    CreatedDate = DateTime.UtcNow
                });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Ok();
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, string decision, string notes, CancellationToken cancellationToken)
    {
        if (decision is not ("reviewed" or "false-positive" or "confirmed-rule-violation" or "insufficient-evidence" or "allowed-object" or "further-review") || string.IsNullOrWhiteSpace(notes) || notes.Trim().Length is < 10 or > 300) return BadRequest();
        var attemptId = await TeacherEvidenceAccess.ReviewableIncidents(db, UserId).Where(l => l.LogId == id).Select(l => l.AttemptId).SingleOrDefaultAsync(cancellationToken);
        if (attemptId is null) return NotFound();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await ProctoringStateService.LockAttemptAsync(db, attemptId.Value, cancellationToken);
            var log = await TeacherEvidenceAccess.ReviewableIncidents(db, UserId).SingleOrDefaultAsync(l => l.LogId == id, cancellationToken);
            if (log is null) return NotFound();
            var prior = await db.ProctoringLogs.Where(l => l.ParentIncidentId == id && l.ModelName == "Teacher review")
                .OrderByDescending(l => l.LogId).FirstOrDefaultAsync(cancellationToken);
            if (prior is not null)
            {
                using var saved = System.Text.Json.JsonDocument.Parse(prior.MachineObservationsJson!);
                var previous = saved.RootElement.GetProperty("decision").GetString();
                if (previous == decision) return RedirectToAction(nameof(Incident), new { id });
                if (previous != "further-review") return Conflict("A final review decision already exists for this incident.");
            }
            if (log.CountsAsViolation && log.AttemptId.HasValue && decision is "false-positive" or "allowed-object")
            {
                if (states is null || !await states.CorrectIncidentAsync(id, UserId, notes.Trim(), cancellationToken)) return StatusCode(409);
            }
            log.IsReviewed = decision != "further-review";
            if (decision is "false-positive" or "allowed-object" &&
                log.IncidentKey?.StartsWith("object:", StringComparison.Ordinal) == true &&
                log.SessionAttemptId is int sessionId)
            {
                var session = await db.ExamSessionAttempts.SingleOrDefaultAsync(
                    s => s.SessionAttemptId == sessionId && s.AttemptId == attemptId, cancellationToken);
                if (session?.ObjectTrackingJson is string tracking)
                {
                    try
                    {
                        var scene = System.Text.Json.JsonSerializer.Deserialize<ObjectSceneState>(tracking);
                        var track = scene?.Tracks.SingleOrDefault(t => t.LogId == id);
                        if (track is not null)
                        {
                            track.Dismissed = true;
                            session.ObjectTrackingJson = System.Text.Json.JsonSerializer.Serialize(scene);
                        }
                    }
                    catch (System.Text.Json.JsonException) { /* Historical incident may outlive its tracker. */ }
                }
            }
            db.ProctoringLogs.Add(new ProctoringLog
            {
                StudentId = log.StudentId,
                ExamId = log.ExamId,
                AttemptId = log.AttemptId,
                SessionAttemptId = log.SessionAttemptId,
                AlertType = ProctoringAlertType.ReviewRequested,
                CountsAsViolation = false,
                PolicyProcessed = true,
                ActionByUserId = UserId,
                ActionByName = User.Identity?.Name,
                CreatedDate = DateTime.UtcNow,
                ParentIncidentId = id,
                ModelName = "Teacher review",
                MachineObservationsJson = System.Text.Json.JsonSerializer.Serialize(new { incidentId = id, decision, reviewer = UserId }),
                Description = $"Incident #{id}: {decision}. {notes.Trim()}"
            });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            if (states is not null) await states.NotifyObservationAsync(id, cancellationToken);
            return RedirectToAction(nameof(Incident), new { id });
        });
    }
}


