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
             l.AlertType == ProctoringAlertType.ExamResumed || l.ModelName == "Student appeal" ||
             l.ModelName == ReviewRequestRules.DecisionModel))
            .OrderBy(l => l.CreatedDate).ThenBy(l => l.LogId).ToListAsync(ct);
        var cause = logs.LastOrDefault(l => l.CountsAsViolation && (block is null || l.LogId < block.LogId));
        var reason = attempt.Exam?.BlockingMode != ExamBlockingMode.MANUAL_ONLY && attempt.StrikeCount > (attempt.Exam?.AllowedWarnings ?? 3) ? $"Exceeding the allowed {(attempt.Exam?.AllowedWarnings ?? 3)} warnings; incident {attempt.StrikeCount} triggered a review lock."
            : cause?.AlertType == ProctoringAlertType.IdentityMismatch ? "Candidate swap — identity mismatch confirmed after fresh rechecks."
            : block?.Description ?? "Your instructor has locked this attempt for review.";
        var blockLogId = logs.Where(l => l.AlertType == ProctoringAlertType.ExamBlocked)
            .Select(l => l.LogId).DefaultIfEmpty(0).Max();
        var request = logs.LastOrDefault(l => l.ModelName == ReviewRequestRules.RequestModel &&
            ReviewRequestRules.BelongsToCurrentLock(l.LogId, blockLogId,
                l.ModelVersion, attempt.StateVersion));
        var decision = request is null ? null : await _context.ProctoringLogs.AsNoTracking()
            .Where(l => l.ParentIncidentId == request.LogId &&
                l.ModelName == ReviewRequestRules.DecisionModel)
            .OrderByDescending(l => l.LogId).FirstOrDefaultAsync(ct);
        string? Evidence(ProctoringLog log, string kind) => null;
        return new BlockedExamViewModel
        {
            AttemptId = attempt.AttemptId,
            ExamTitle = attempt.Exam!.ExamTitle,
            StudentName = await _context.Users.Where(u => u.Id == StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Candidate",
            FrozenSeconds = session?.RemainingTimeSeconds ?? 0,
            LockedAt = block?.CreatedDate ?? session?.FrozenAtUtc?.ToLocalTime(),
            SavedAnswers = await _context.StudentAnswers.CountAsync(a => a.SessionAttempt!.AttemptId == attempt.AttemptId && a.SelectedOption != null && a.SelectedOption != "", ct),
            Reason = reason,
            ReviewRequested = request is not null,
            ReviewRejected = decision?.ModelVersion == "rejected",
            ReviewDecisionReason = decision?.Description,
            Incidents = logs.Select(log => {
                var recording = log.RecordingPath ?? (IsVideo(log.ScreenshotPath) ? log.ScreenshotPath : null);
                var video = IsVideo(recording);
                var screenshot = !string.IsNullOrEmpty(log.ScreenshotPath) && !IsVideo(log.ScreenshotPath);
                var speech = log.AlertType is ProctoringAlertType.TalkingDetected or ProctoringAlertType.AnswerPromptSpeech;
                return new BlockedExamIncident(log.LogId, log.FrameCapturedAt ?? log.CreatedDate,
                    log.AlertType == ProctoringAlertType.ExamBlocked ? "Final action" : log.WarningNumber is int number ? (number >= 4 ? "Violation 4 / lock trigger" : $"Strike {number}")
                        : log.CountsAsViolation ? "Violation" : "Audit update",
                    AuditCategory(log), log.AlertTypeDisplay,
                    screenshot ? Evidence(log, "screenshot") : null,
                    recording is not null && (!video || speech && log.RecordingIncludesAudio) ? Evidence(log, "recording") : null,
                    log.VideoEvidencePath != null ? Evidence(log, "video") : video ? Evidence(log, "recording") : null, log.AlertType == ProctoringAlertType.ExamBlocked, null, log.SemanticSimilarity, log.MatchedQuestionId);
            }).ToList()
        };
    }

    private static bool IsVideo(string? path) => path is not null && new[] { ".webm", ".mp4" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    private static string AuditCategory(ProctoringLog log) => log.AlertType switch
    {
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
        await Task.CompletedTask;
        // Explicit shares are served by SharedEvidence with a recipient-bound grant.
        return NotFound();
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
            var blockLogId = await _context.ProctoringLogs.AsNoTracking()
                .Where(l => l.AttemptId == attemptId && l.AlertType == ProctoringAlertType.ExamBlocked)
                .OrderByDescending(l => l.LogId).Select(l => l.LogId)
                .FirstOrDefaultAsync(cancellationToken);
            appeal = await _context.ProctoringLogs.FirstOrDefaultAsync(l => l.AttemptId == attemptId &&
                l.StudentId == StudentId && l.AlertType == ProctoringAlertType.ReviewRequested &&
                l.ModelName == ReviewRequestRules.RequestModel &&
                (blockLogId > 0 ? l.LogId > blockLogId && l.ModelVersion != null &&
                    l.ModelVersion.StartsWith("lock:") : l.ModelVersion == version), cancellationToken);
            duplicate = appeal is not null;
            if (appeal is null)
            {
                appeal = new ProctoringLog
                {
                    AttemptId = attemptId,
                    StudentId = StudentId,
                    ExamId = attempt.ExamId,
                    SessionAttemptId = await _context.ExamSessionAttempts.Where(s => s.AttemptId == attemptId).OrderByDescending(s => s.SessionAttemptId).Select(s => (int?)s.SessionAttemptId).FirstOrDefaultAsync(cancellationToken),
                    AlertType = ProctoringAlertType.ReviewRequested,
                    ModelName = "Student appeal",
                    ModelVersion = version,
                    Description = "The candidate requests instructor review of the violation audit and authorization to resume.",
                    CountsAsViolation = false,
                    PolicyProcessed = true,
                    ClientEventId = Guid.NewGuid().ToString("N"),
                    CreatedDate = DateTime.Now
                };
                _context.ProctoringLogs.Add(appeal); await _context.SaveChangesAsync(cancellationToken);
            }
            name = await _context.Users.Where(u => u.Id == StudentId).Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
        if (appeal is null) return Conflict(new { message = "The attempt is unavailable or has already been released. Check its status." });
        // A database-backed Student appeal (LogId) is the only notification identity.
        // Do not push again when this same lock's request is submitted twice.
        var notified = false;
        if (!duplicate)
        {
            try
            {
                var reviewers = await _context.ExamInvigilators.AsNoTracking().Where(i =>
                    i.ExamId == appeal.ExamId && i.IsActive && i.CanMonitor &&
                    i.AcceptedAtUtc != null && i.RevokedAtUtc == null)
                    .Select(i => i.TeacherId).ToListAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(owner)) reviewers.Add(owner);
                var groups = reviewers.Distinct(StringComparer.Ordinal)
                    .Select(ProctoringHub.TeacherGroup).ToArray();
                if (groups.Length > 0)
                {
                    await hub.Clients.Groups(groups).SendAsync("LockReviewRequested", new
                    {
                        attemptId,
                        appealId = appeal.LogId,
                        candidateName = name ?? "Candidate",
                        examId = appeal.ExamId,
                        reviewUrl = Url.Action("Details", "ReviewRequests", new
                        {
                            area = "Teacher", id = appeal.LogId
                        })
                    }, cancellationToken);
                    notified = true;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                HttpContext.RequestServices.GetService<ILogger<ExamController>>()?
                    .LogWarning(error, "Appeal {Id} saved; live notification unavailable", appeal.LogId);
            }
        }
        return Json(new
        {
            ok = true,
            duplicate,
            notified,
            requestId = appeal.LogId,
            message = duplicate
                ? "A review request for this lock is already saved in your teacher's inbox."
                : notified
                    ? "Review request saved and your examiner was alerted. Only an authorized teacher can resume this attempt."
                    : "Review request saved. Your teacher can find it in the inbox even if live notifications are unavailable."
        });
    }
}
