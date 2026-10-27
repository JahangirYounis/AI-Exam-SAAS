using AiExamProctoring.Hubs;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using AiExamProctoring.ViewModels.Student;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Student.Controllers;

public partial class ExamController
{
    private async Task<BlockedExamViewModel> BuildBlockedAuditAsync(ExamAttempt attempt, ProctoringLog? block, CancellationToken ct)
    {
        var session = await _context.ExamSessionAttempts.AsNoTracking().Where(s => s.AttemptId == attempt.AttemptId)
            .OrderByDescending(s => s.SessionAttemptId).FirstOrDefaultAsync(ct);
        var logs = await _context.ProctoringLogs.AsNoTracking().Where(l => l.AttemptId == attempt.AttemptId && l.StudentId == StudentId &&
            ((l.CountsAsViolation && l.AlertType != ProctoringAlertType.IdentityVerified && l.AlertType != ProctoringAlertType.SessionRecording) ||
             l.WarningNumber != null || l.AlertType == ProctoringAlertType.ExamBlocked || l.AlertType == ProctoringAlertType.ExamUnblocked ||
             l.AlertType == ProctoringAlertType.ExamResumed || l.ModelName == "Student appeal"))
            .OrderBy(l => l.CreatedDate).ThenBy(l => l.LogId).ToListAsync(ct);
        var cause = logs.LastOrDefault(l => l.CountsAsViolation && (block is null || l.LogId < block.LogId));
        var reason = attempt.Exam?.BlockingMode != ExamBlockingMode.MANUAL_ONLY && attempt.StrikeCount >= 4 ? "Exceeding maximum allowed strikes (3/3). A fourth violation triggered the hard lock."
            : cause?.AlertType == ProctoringAlertType.IdentityMismatch ? "Candidate swap — identity mismatch confirmed after fresh rechecks."
            : block?.Description ?? "Your instructor has locked this attempt for review.";
        string? Evidence(ProctoringLog log, string kind) => Url.Action(nameof(AuditEvidence), "Exam", new { area = "Student", attemptId = attempt.AttemptId, id = log.LogId, kind });
        return new BlockedExamViewModel {
            AttemptId = attempt.AttemptId, ExamTitle = attempt.Exam!.ExamTitle,
            StudentName = await _context.Users.Where(u => u.Id == StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Candidate",
            FrozenSeconds = session?.RemainingTimeSeconds ?? 0, LockedAt = block?.CreatedDate ?? session?.FrozenAtUtc?.ToLocalTime(),
            SavedAnswers = await _context.StudentAnswers.CountAsync(a => a.SessionAttempt!.AttemptId == attempt.AttemptId && a.SelectedOption != null && a.SelectedOption != "", ct),
            Reason = reason, ReviewRequested = logs.Any(l => l.ModelName == "Student appeal" && l.ModelVersion == $"lock:{attempt.StateVersion}"),
            Incidents = logs.Select(log => {
                var recording = log.RecordingPath ?? (IsVideo(log.ScreenshotPath) ? log.ScreenshotPath : null);
                var video = IsVideo(recording);
                var screenshot = !string.IsNullOrEmpty(log.ScreenshotPath) && !IsVideo(log.ScreenshotPath);
                var speech = log.AlertType is ProctoringAlertType.TalkingDetected or ProctoringAlertType.AnswerPromptSpeech;
                return new BlockedExamIncident(log.LogId, log.FrameCapturedAt ?? log.CreatedDate,
                    log.AlertType == ProctoringAlertType.ExamBlocked ? "Final action" : log.WarningNumber is int number ? (number >= 4 ? "Violation 4 / lock trigger" : $"Strike {number}")
                        : log.CountsAsViolation ? "Violation" : "Audit update",
                    AuditCategory(log), log.Description ?? "No additional description was recorded.",
                    screenshot ? Evidence(log, "screenshot") : null,
                    recording is not null && (!video || speech && log.RecordingIncludesAudio) ? Evidence(log, "recording") : null,
                    log.VideoEvidencePath != null ? Evidence(log, "video") : video ? Evidence(log, "recording") : null, log.AlertType == ProctoringAlertType.ExamBlocked, log.SpeechTranscript, log.SemanticSimilarity, log.MatchedQuestionId);
            }).ToList()
        };
    }

    private static bool IsVideo(string? path) => path is not null && new[] { ".webm", ".mp4" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    private static string AuditCategory(ProctoringLog log) => log.AlertType switch {
        ProctoringAlertType.IdentityMismatch => "Candidate swap / identity mismatch",
        ProctoringAlertType.GazeAway or ProctoringAlertType.LookAway => "Gaze / attention away",
        ProctoringAlertType.AnswerPromptSpeech => "Question discussion speech",
        ProctoringAlertType.ElectronicDeviceDetected => log.Description?.StartsWith("[OBJECT_") == true && log.Description.IndexOf(']') is var end && end > 8
            ? "Banned object: " + log.Description[8..end].Replace('_', ' ').ToLowerInvariant() : "Banned object",
        ProctoringAlertType.ExamBlocked => "Exam hard-locked",
        ProctoringAlertType.ExamUnblocked => "Instructor authorized unlock",
        ProctoringAlertType.ExamResumed => "Exam resumed",
        ProctoringAlertType.ReviewRequested => "Instructor review requested",
        _ => log.AlertTypeDisplay
    };

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AuditEvidence(int attemptId, int id, string kind, CancellationToken cancellationToken)
    {
        if (StudentId is null) return Unauthorized();
        if (kind is not ("screenshot" or "recording" or "video")) return BadRequest();
        var log = await _context.ProctoringLogs.AsNoTracking().FirstOrDefaultAsync(l => l.LogId == id && l.AttemptId == attemptId &&
            l.StudentId == StudentId && l.Attempt!.StudentId == StudentId, cancellationToken);
        if (log is null) return NotFound();
        var key = kind == "video" ? log.VideoEvidencePath : kind == "screenshot" ? log.ScreenshotPath : log.RecordingPath ?? (IsVideo(log.ScreenshotPath) ? log.ScreenshotPath : null);
        var file = await _evidenceStore.OpenReadAsync(key, cancellationToken);
        if (file is null) return NotFound();
        Response.Headers.CacheControl = "private,no-store,max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AppealLock(int attemptId, [FromServices] IHubContext<ProctoringHub> hub, CancellationToken cancellationToken)
    {
        if (StudentId is null) return Unauthorized();
        ProctoringLog? appeal = null;
        string? owner = null;
        string? name = null;
        var duplicate = false;
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            appeal = null; owner = null; duplicate = false;
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            await ProctoringStateService.LockAttemptAsync(_context, attemptId, cancellationToken);
            var attempt = await _context.ExamAttempts.Include(a => a.Exam)
                .FirstOrDefaultAsync(a => a.AttemptId == attemptId && a.StudentId == StudentId && a.Status == AttemptStatus.ProctoringBlocked, cancellationToken);
            if (attempt?.Exam is null) return;
            owner = attempt.Exam.CreatedBy;
            var version = $"lock:{attempt.StateVersion}";
            appeal = await _context.ProctoringLogs.FirstOrDefaultAsync(l => l.AttemptId == attemptId && l.StudentId == StudentId &&
                l.ModelName == "Student appeal" && l.ModelVersion == version, cancellationToken);
            duplicate = appeal is not null;
            if (appeal is null) {
                appeal = new ProctoringLog { AttemptId = attemptId, StudentId = StudentId, ExamId = attempt.ExamId,
                    SessionAttemptId = await _context.ExamSessionAttempts.Where(s => s.AttemptId == attemptId).OrderByDescending(s => s.SessionAttemptId).Select(s => (int?)s.SessionAttemptId).FirstOrDefaultAsync(cancellationToken),
                    AlertType = ProctoringAlertType.ReviewRequested, ModelName = "Student appeal", ModelVersion = version,
                    Description = "The candidate requests instructor review of the violation audit and authorization to resume.",
                    CountsAsViolation = false, PolicyProcessed = true, ClientEventId = Guid.NewGuid().ToString("N"), CreatedDate = DateTime.Now };
                _context.ProctoringLogs.Add(appeal); await _context.SaveChangesAsync(cancellationToken);
            }
            name = await _context.Users.Where(u => u.Id == StudentId).Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
        if (appeal is null) return Conflict(new { message = "The attempt is unavailable or has already been released. Check its status." });
        var notified = false;
        try {
            await hub.Clients.Group(ProctoringHub.TeacherGroup(owner!)).SendAsync("LockReviewRequested", new {
                attemptId, appealId = appeal.LogId, candidateName = name ?? "Candidate", examId = appeal.ExamId,
                reviewUrl = Url.Action("ExamFolder", "Monitoring", new { area = "Teacher", studentId = StudentId, examId = appeal.ExamId })
            }, cancellationToken);
            notified = true;
        } catch (Exception error) when (error is not OperationCanceledException) {
            HttpContext.RequestServices.GetService<ILogger<ExamController>>()?.LogWarning(error, "Appeal {Id} saved; live notification unavailable", appeal.LogId);
        }
        return Json(new { ok = true, duplicate, notified, message = notified
            ? "Review request saved and sent to your instructor's dashboard. Only your instructor can unlock this attempt."
            : "Review request saved. Live notification is unavailable; your instructor can still find it in the audit log." });
    }
}
