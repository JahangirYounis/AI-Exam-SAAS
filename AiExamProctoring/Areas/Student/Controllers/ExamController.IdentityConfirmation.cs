using System.Data;
using System.Security.Cryptography;
using AiExamProctoring.Models;
using AiExamProctoring.Services.FaceIdentity;
using AiExamProctoring.Services.Proctoring;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Student.Controllers;

public partial class ExamController
{
    // Native inference is already complete. Serialize only the short decision write
    // across browser tabs and application instances using the same attempt lock.
    private async Task<(int? ConfirmedSampleId, int Count, DateTime? StartedAt)> ConfirmIdentityMismatchAsync(int attemptId, int sessionId,
        DateTime capturedAt, string frameHash, decimal similarity, CancellationToken ct, string? evidenceKey = null, Guid? sequenceId = null)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await ProctoringStateService.LockAttemptAsync(_context, attemptId, ct);
            var attempt = await _context.ExamAttempts.AsNoTracking().SingleAsync(x => x.AttemptId == attemptId && x.StudentId == StudentId, ct);
            if (attempt.Status != AttemptStatus.InProgress ||
                !await IsCurrentIdentityObservationAsync(attemptId, sessionId, capturedAt, ct) ||
                attempt.TeacherResumedAtUtc is DateTime resumed && capturedAt.ToUniversalTime() <= resumed) return ((int?)null, 0, (DateTime?)null);
            var previous = await _context.ProctoringLogs.AsNoTracking()
                .Where(x => x.StudentId == StudentId && x.SessionAttemptId == sessionId &&
                    (x.AlertType == ProctoringAlertType.IdentityVerified || x.AlertType == ProctoringAlertType.IdentityMismatch || x.ModelName == "Identity monitoring health"))
                .OrderByDescending(x => x.FrameCapturedAt ?? x.CreatedDate)
                .ThenByDescending(x => x.LogId).FirstOrDefaultAsync(ct);
            if (previous is not null && (previous.FrameCapturedAt ?? previous.CreatedDate).ToUniversalTime() >=
                capturedAt.ToUniversalTime()) return ((int?)null, 0, (DateTime?)null);
            if (attempt.TeacherResumedAtUtc is DateTime release && previous?.CreatedDate.ToUniversalTime() <= release) previous = null;
            if (previous?.FrameCapturedAt is DateTime previousCapture &&
                !await IsCurrentIdentityObservationAsync(attemptId, sessionId, previousCapture, ct))
                previous = null;
            var next = IdentityRecheckPolicy.Advance(previous?.Description, previous?.CreatedDate, DateTime.Now, frameHash, sequenceId);
            var description = IdentityRecheckPolicy.Describe(next);
            if (description == previous?.Description) return ((int?)null, next.Count, (DateTime?)null);
            // Persist each accepted independent sample for an auditable sequence.
            // Low-severity pending checks are never cheating strikes.
            var sample = new ProctoringLog
            {
                StudentId = StudentId!,
                ExamId = attempt.ExamId,
                AttemptId = attemptId,
                SessionAttemptId = sessionId,
                AlertType = ProctoringAlertType.IdentityMismatch,
                Severity = ProctoringSeverity.Low,
                ScreenshotPath = evidenceKey,
                Description = description,
                ConfidenceScore = similarity,
                FrameCapturedAt = capturedAt,
                MachineObservationsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    identityConfirmation = "pending_fresh_batches",
                    confirmedBatches = next.Count,
                    requiredBatches = IdentityRecheckPolicy.RequiredMismatchBatches
                }),
                ModelName = _faceOptions.ModelName,
                ModelVersion = _faceOptions.ModelVersion,
                ClientEventId = Guid.NewGuid().ToString("N"),
                CountsAsViolation = false,
                PolicyPending = false,
                PolicyProcessed = true,
                CreatedDate = DateTime.Now
            };
            _context.ProctoringLogs.Add(sample);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (IdentityRecheckPolicy.IsConfirmed(next) ? (int?)sample.LogId : null, next.Count,
                previous?.FrameCapturedAt);
        });
    }

    // Called under the attempt lock so an absence/return committed during inference
    // cannot be crossed by an older identity response or mismatch sequence.
    private async Task<bool> IsCurrentIdentityObservationAsync(int attemptId, int sessionId, DateTime capturedAt, CancellationToken ct)
    {
        var session = await _context.ExamSessionAttempts.AsNoTracking()
            .Where(s => s.SessionAttemptId == sessionId && s.AttemptId == attemptId &&
                s.Status == AttemptStatus.InProgress && s.FrozenAtUtc == null)
            .Select(s => new { s.SessionAttemptId }).SingleOrDefaultAsync(ct);
        if (session is null) return false;
        var captured = capturedAt.ToUniversalTime();
        return !await _context.ProctoringLogs.AnyAsync(l => l.AttemptId == attemptId &&
            l.SessionAttemptId == sessionId && l.IncidentKey == "absence" &&
            (l.IncidentEndedAtUtc == null || l.IncidentEndedAtUtc >= captured), ct);
    }
}
