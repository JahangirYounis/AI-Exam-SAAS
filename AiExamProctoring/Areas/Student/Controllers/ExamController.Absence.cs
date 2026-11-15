
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Student.Controllers;
public partial class ExamController
{
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(20_000_000)]
    [Route("/Student/Exam/AbsenceEvidence")]
    [Route("/Student/Exam/IncidentEvidence")]
    public async Task<IActionResult> AbsenceEvidence(int logId, string uploadId, string stage, DateTime? capturedAt, string? screenshot, IFormFile? media, CancellationToken ct, string? browserEventId = null, int? attemptId = null, int? sessionAttemptId = null, string? metadata = null)
    {
        if (StudentId is null) return Unauthorized();
        if (!_proctoringOptions.CaptureEvidence || !Guid.TryParse(uploadId, out var upload) || stage is not ("before" or "during" or "return" or "video" or "screen-image" or "camera-original" or "screen-original" or "composite" or "audio-original" or "absence-segment")) return BadRequest();
        if (metadata?.Length > 8000) return BadRequest();
        var originalIncludesMicrophone = false;
        long? segmentStart = null;
        if (metadata is not null)
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(metadata);
                if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return BadRequest();
                originalIncludesMicrophone = json.RootElement.TryGetProperty("includesMicrophone", out var audio) &&
                    audio.ValueKind == System.Text.Json.JsonValueKind.True;
                if (stage == "absence-segment")
                {
                    if (!json.RootElement.TryGetProperty("startedAt", out var start) ||
                        !json.RootElement.TryGetProperty("endedAt", out var end) ||
                        start.ValueKind != System.Text.Json.JsonValueKind.Number ||
                        end.ValueKind != System.Text.Json.JsonValueKind.Number ||
                        !start.TryGetInt64(out var from) || !end.TryGetInt64(out var until) ||
                        from < 0 || until <= from || until > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() ||
                        until - from > 180_000 ||
                        !json.RootElement.TryGetProperty("stopReason", out var reason) ||
                        reason.ValueKind != System.Text.Json.JsonValueKind.String ||
                        reason.GetString() is not ("segment_rollover" or "physical_return" or "exam_blocked"
                            or "exam_ended" or "camera_lost" or "recorder_error" or "recording_limit")) return BadRequest();
                    segmentStart = from;
                }
            }
            catch (System.Text.Json.JsonException) { return BadRequest(); }
        }
        if (browserEventId is not null)
        {
            if (!Guid.TryParse(browserEventId, out var browserId) || attemptId is null || sessionAttemptId is null) return BadRequest();
            logId = await _context.ProctoringLogs.Where(l => l.ClientEventId == "browser:" + browserId.ToString("N") && l.StudentId == StudentId && l.AttemptId == attemptId && l.SessionAttemptId == sessionAttemptId && l.IncidentKey == "browser").Select(l => l.LogId).SingleOrDefaultAsync(ct);
        }
        var screenAttachment = stage is "screen-image" or "screen-original" or "composite";
        // Attach original camera/screen media to *all* student-owned, server-created
        // root incidents, not only absence/object incidents. Earlier the browser
        // queued head/gaze, speech, identity and device video but this endpoint
        // returned 404, causing permanent retries and missing Teacher evidence.
        var evidenceAlerts = new[] {
            ProctoringAlertType.IdentityMismatch, ProctoringAlertType.FaceNotDetected,
            ProctoringAlertType.FaceNotVisible, ProctoringAlertType.MultipleFaces,
            ProctoringAlertType.AdditionalPersonObservation,
            ProctoringAlertType.HeadAway, ProctoringAlertType.GazeAway,
            ProctoringAlertType.LookAway, ProctoringAlertType.LookingAwaySpeaking,
            ProctoringAlertType.TalkingDetected, ProctoringAlertType.AnswerPromptSpeech,
            ProctoringAlertType.AudioDetected, ProctoringAlertType.ElectronicDeviceDetected,
            ProctoringAlertType.ObjectObservation, ProctoringAlertType.CameraAccessLost,
            ProctoringAlertType.MicrophoneAccessLost, ProctoringAlertType.CameraMonitoringInterrupted,
            ProctoringAlertType.AiMonitoringUnavailable, ProctoringAlertType.TabSwitch,
            ProctoringAlertType.FullScreenExit, ProctoringAlertType.ScreenShareLost,
            ProctoringAlertType.ScreenCaptureUnavailable
        };
        var parent = await _context.ProctoringLogs.AsNoTracking().SingleOrDefaultAsync(l =>
            l.LogId == logId && l.StudentId == StudentId && l.ParentIncidentId == null &&
            l.AttemptId != null && l.SessionAttemptId != null &&
            (evidenceAlerts.Contains(l.AlertType) || l.IncidentKey == "absence" ||
             l.IncidentKey == "persons" || l.IncidentKey == "browser" ||
             (l.IncidentKey != null && l.IncidentKey.StartsWith("object:"))), ct);
        if ((attemptId.HasValue && parent?.AttemptId != attemptId) || (sessionAttemptId.HasValue && parent?.SessionAttemptId != sessionAttemptId)) return NotFound();
        if (parent?.AttemptId is not int attempt || parent.ExamId is not int exam) return NotFound();
        if (stage == "absence-segment" && (parent.IncidentKey != "absence" || segmentStart is null ||
            attemptId is null || sessionAttemptId is null)) return BadRequest();
        if (stage == "absence-segment" && media?.ContentType != "video/webm") return BadRequest();
        var speechAudio = stage == "audio-original" && parent.IncidentKey is "speech" or "ambient";
        if (speechAudio && (attemptId is null || sessionAttemptId is null ||
            media?.ContentType != "audio/wav" || media.Length > 200_000)) return BadRequest();
        // Server ownership and unique key bind every retry to its immutable incident.
        var key = segmentStart is long startTime ? $"absence-media:{logId}:{startTime}" : "absence:" + upload.ToString("N");
        string? path = null;
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () => {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            await ProctoringStateService.LockEvidenceUploadsAsync(_context, attempt, ct);
            var duplicate = await _context.ProctoringLogs.SingleOrDefaultAsync(l => l.ClientEventId == key, ct);
            if (duplicate is not null) return duplicate.ParentIncidentId == logId ? Json(new { ok = true, acknowledged = true }) : Conflict();
            // The original code permitted 1,200 separate attachments for one episode.
            // A multi-second camera absence could therefore fill the evidence folder
            // with near-identical WebM clips. A suppressed media duplicate is ACKed
            // so the durable browser outbox does not retry it indefinitely.
            var existing = await _context.ProctoringLogs.AsNoTracking()
                .Where(l => l.ParentIncidentId == logId && l.IncidentKey == "absence_evidence")
                .GroupBy(l => 1)
                .Select(g => new {
                    Total = g.Count(),
                    SameStage = g.Count(l => l.ModelName == stage)
                }).FirstOrDefaultAsync(ct);
            if (speechAudio ? (existing?.SameStage ?? 0) >= 120 :
                stage == "absence-segment" ? (existing?.SameStage ?? 0) >= 240 :
                !EvidenceCapturePolicy.MayStore(stage, parent.Severity, existing?.SameStage ?? 0, existing?.Total ?? 0))
            {
                if (stage == "absence-segment" || speechAudio)
                    return StatusCode(422, new { ok = false, message = "Incident evidence storage limit reached." });
                await tx.CommitAsync(ct);
                return Json(new { ok = true, acknowledged = true, suppressed = true,
                    message = "Already enough supporting media for this incident." });
            }
            var video = stage is "video" or "camera-original" or "screen-original" or "composite" or "audio-original" or "absence-segment";
            int? absenceIncidentId = parent.IncidentKey == "absence" ? parent.LogId : null;
            path ??= video ? await _evidenceStore.SaveUploadAsync(StudentId, exam, attempt, media, ct, absenceIncidentId)
                : await _evidenceStore.SaveDataUrlAsync(StudentId, exam, attempt, screenshot, ct, absenceIncidentId);
            if (path is null) return BadRequest(new { ok = false, message = "Evidence could not be stored." });
            var asset = new ProctoringLog
            {
                ParentIncidentId = logId,
                StudentId = StudentId,
                ExamId = exam,
                AttemptId = attempt,
                SessionAttemptId = parent.SessionAttemptId,
                AlertType = screenAttachment ? ProctoringAlertType.BrowserEvidence : parent.IncidentKey?.StartsWith("object:") == true ? ProctoringAlertType.ObjectEvidence : parent.IncidentKey == "browser" ? ProctoringAlertType.BrowserEvidence : parent.IncidentKey == "persons" ? ProctoringAlertType.PersonEvidence : parent.IncidentKey == "absence" ? ProctoringAlertType.AbsenceEvidence : ProctoringAlertType.Other,
                ClientEventId = key,
                IncidentKey = "absence_evidence",
                ModelName = stage,
                ModelVersion = "incident-evidence-v1",
                Description = stage == "audio-original" ? "Original microphone segment; ordered by capture time under the canonical audio incident." : stage == "composite" ? "Derived synchronized review copy; original sources are stored separately." : stage.StartsWith("screen") ? "Original authorized screen evidence for the linked incident." : "Original camera evidence for the linked incident.",
                CountsAsViolation = false,
                PolicyProcessed = true,
                CreatedDate = DateTime.UtcNow,
                FrameCapturedAt = segmentStart is long timestamp ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime : capturedAt?.ToUniversalTime(),
                MachineObservationsJson = metadata,
                ScreenshotPath = video ? null : path,
                VideoEvidencePath = video ? path : null,
                RecordingIncludesAudio = video && originalIncludesMicrophone &&
                    stage is ("screen-original" or "camera-original" or "composite" or "audio-original" or "absence-segment")
            };
            _context.ProctoringLogs.Add(asset);
            await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Json(new { ok = true, acknowledged = true, evidenceId = asset.LogId });
        });
    }
}
