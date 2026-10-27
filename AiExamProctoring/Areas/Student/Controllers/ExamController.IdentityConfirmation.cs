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
    private async Task<bool> ConfirmIdentityMismatchAsync(int attemptId, int sessionId,
        DateTime capturedAt, string frameHash, decimal similarity, CancellationToken ct)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await ProctoringStateService.LockAttemptAsync(_context, attemptId, ct);
            var attempt = await _context.ExamAttempts.AsNoTracking().SingleAsync(x => x.AttemptId == attemptId && x.StudentId == StudentId, ct);
            if (attempt.Status != AttemptStatus.InProgress ||
                attempt.TeacherResumedAtUtc is DateTime resumed && capturedAt.ToUniversalTime() <= resumed) return false;
            var previous = await _context.ProctoringLogs.AsNoTracking()
                .Where(x => x.StudentId == StudentId && x.SessionAttemptId == sessionId &&
                    (x.AlertType == ProctoringAlertType.IdentityVerified || x.AlertType == ProctoringAlertType.IdentityMismatch))
                .OrderByDescending(x => x.LogId).FirstOrDefaultAsync(ct);
            if (previous?.FrameCapturedAt >= capturedAt) return false;
            if (attempt.TeacherResumedAtUtc is DateTime release && previous?.CreatedDate.ToUniversalTime() <= release) previous = null;
            var next = IdentityRecheckPolicy.Advance(previous?.Description, previous?.CreatedDate, DateTime.Now, frameHash);
            var description = IdentityRecheckPolicy.Describe(next);
            if (description == previous?.Description) return false;
            _context.ProctoringLogs.Add(new ProctoringLog {
                StudentId = StudentId!, ExamId = attempt.ExamId, AttemptId = attemptId, SessionAttemptId = sessionId,
                AlertType = ProctoringAlertType.IdentityMismatch, Severity = ProctoringSeverity.Low,
                Description = description, ConfidenceScore = similarity, FrameCapturedAt = capturedAt,
                ModelName = _faceOptions.ModelName, ModelVersion = _faceOptions.ModelVersion,
                ClientEventId = Guid.NewGuid().ToString("N"), CountsAsViolation = false,
                PolicyPending = false, PolicyProcessed = true, CreatedDate = DateTime.Now });
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return next.Count >= 3;
        });
    }
}
