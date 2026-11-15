using System.Security.Cryptography;
using System.Text.Json;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services.Results;

public sealed record CertificateSnapshot(string Number, string StudentName, string CandidateNumber, string RollNumber,
    string Examination, DateTime ExaminationDate, decimal Obtained, decimal Total, decimal Percentage, bool Passed,
    string Grade, string DocumentType, string Completion, string? PhotoSha256, DateTime IssuedAtUtc);
public sealed record CertificateVerification(string Number, string Examination, string Status, string DocumentType, DateTime IssuedAtUtc);

public static class CertificateRecords
{
    public const string TokenPurpose = "Examination.Certificate.Verification.v2";
    public static string Status(ExamResult result) => result.CertificateRevokedAtUtc.HasValue ? "Revoked" :
        result.CertificateExpiresAtUtc <= DateTime.UtcNow ? "Expired" : result.Attempt?.ResultReleasedAtUtc == null ? "Invalid" : "Valid";
    public static bool ValidPhoto(AttemptIdentityPhoto? photo) => photo is { ImageBytes.Length: > 0 } &&
        string.Equals(Convert.ToHexString(SHA256.HashData(photo.ImageBytes)), photo.Sha256, StringComparison.OrdinalIgnoreCase);

    public static async Task<ExamResult> IssueAsync(ApplicationDbContext db, int resultId, string studentId, CancellationToken ct)
    {
        var attemptId = await db.ExamResults.Where(r => r.ResultId == resultId && r.Attempt!.StudentId == studentId &&
            r.Attempt.ResultReleasedAtUtc != null).Select(r => (int?)r.AttemptId).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException();
        return await ExamResultFinalizationService.WriteAsync(db, attemptId, async () =>
        {
            var result = await db.ExamResults.Include(r => r.Attempt)!.ThenInclude(a => a!.Student)
                .Include(r => r.Attempt)!.ThenInclude(a => a!.Exam).SingleAsync(r => r.ResultId == resultId, ct);
            var attempt = result.Attempt!;
            if (attempt.StudentId != studentId || attempt.ResultReleasedAtUtc == null || !attempt.CompletedAt.HasValue ||
                attempt.Status is AttemptStatus.InProgress or AttemptStatus.NotStarted or AttemptStatus.Cancelled)
                throw new InvalidOperationException("Only finalized, released results may be issued.");
            if (await ExamResultFinalizationService.PendingIntegrity(db, attemptId).AnyAsync(ct))
                throw new InvalidOperationException("Teacher review of outstanding integrity alerts is required before certificate issuance.");
            var requiresApproval = attempt.Status == AttemptStatus.ProctoringBlocked || attempt.IsDisqualified || attempt.SubmittedForViolations ||
                await ExamResultFinalizationService.IntegrityIncidents(db, attemptId).AnyAsync(ct);
            if (requiresApproval && string.IsNullOrWhiteSpace(attempt.ResultReviewedBy))
                throw new InvalidOperationException("The Teacher must approve the reviewed result before this document can be issued.");
            if (result.CertificateReference != null) return result;
            var photo = await db.AttemptIdentityPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.AttemptId == attemptId, ct);
            if (photo != null && !ValidPhoto(photo)) throw new InvalidOperationException("The original attempt photo failed its integrity check. Contact the examination owner.");
            if (photo != null) {
                try { QuestPDF.Infrastructure.Image.FromBinaryData(photo.ImageBytes); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { throw new InvalidOperationException("The original attempt image is unreadable; it will not be replaced.", ex); }
            }
            var complete = (attempt.Status is AttemptStatus.Completed or AttemptStatus.AutoSubmitted) && !attempt.IsDisqualified;
            var required = await db.ExamSessions.Where(s => s.ExamId == attempt.ExamId && s.IsActive).Select(s => s.SessionId).ToListAsync(ct);
            var finished = await db.ExamSessionAttempts.Where(s => s.AttemptId == attemptId &&
                (s.Status == AttemptStatus.Completed || s.Status == AttemptStatus.AutoSubmitted)).Select(s => s.SessionId).ToListAsync(ct);
            complete = complete && required.Count > 0 && required.All(finished.Contains);
            var reviewed = await ExamResultFinalizationService.IntegrityIncidents(db, attemptId).AnyAsync(l => l.IsReviewed, ct);
            var type = complete && photo != null ? "Examination result certificate" : "Finalized result statement";
            var completion = !complete ? "Examination interrupted or blocked; saved-answer marks finalized. This is not a completion certificate." :
                reviewed ? "Examination completed with reviewed integrity observations." : "Examination completed; academic result recorded.";
            if (complete && photo == null) completion += " Original attempt photograph unavailable; completion certificate not issued.";
            var roll = await db.ExamAssignments.Where(a => a.ExamId == attempt.ExamId && a.StudentId == studentId).Select(a => a.RollNumber).SingleOrDefaultAsync(ct);
            result.CertificateReference = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            result.CertificateNumber ??= "AIE-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            result.CertificateIssuedAtUtc = DateTime.UtcNow;
            var snapshot = new CertificateSnapshot(result.CertificateNumber, attempt.Student!.FullName, attempt.Student.CandidateNumber ?? "Not assigned",
                roll ?? attempt.Student.CandidateNumber ?? "Not assigned", attempt.Exam!.ExamTitle, attempt.StartedAt,
                result.TotalMarksObtained, result.TotalMarks, result.Percentage, result.IsPassed,
                StudentResultAccess.GradeLabel(result.Percentage, attempt.Exam.PassingPercentage), type, completion, photo?.Sha256, result.CertificateIssuedAtUtc.Value);
            result.CertificateSnapshotJson = JsonSerializer.Serialize(snapshot);
            db.ProctoringLogs.Add(Audit(attempt, studentId, "Issued", type));
            return result;
        }, ct);
    }

    public static async Task RevokeAsync(ApplicationDbContext db, int id, string teacherId, string reason, CancellationToken ct)
    {
        var attemptId = await db.ExamResults.Where(r => r.ResultId == id && r.Attempt!.Exam!.CreatedBy == teacherId).Select(r => (int?)r.AttemptId).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException();
        await ExamResultFinalizationService.WriteAsync(db, attemptId, async () =>
        {
            var result = await db.ExamResults.Include(r => r.Attempt)!.ThenInclude(a => a!.Exam).SingleAsync(r => r.ResultId == id, ct);
            if (result.Attempt!.Exam!.CreatedBy != teacherId) throw new UnauthorizedAccessException();
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 1000) throw new ArgumentException("Provide a reason of 10–1000 characters.");
            if (result.CertificateReference == null) throw new InvalidOperationException("No certificate has been issued.");
            if (result.CertificateRevokedAtUtc != null) return false;
            result.CertificateRevokedAtUtc = DateTime.UtcNow; result.CertificateRevokedBy = teacherId; result.CertificateRevocationReason = reason.Trim();
            db.ProctoringLogs.Add(Audit(result.Attempt, teacherId, "Revoked", reason.Trim()));
            return true;
        }, ct);
    }
    private static ProctoringLog Audit(ExamAttempt a, string actor, string decision, string reason) => new() {
        AttemptId=a.AttemptId, ExamId=a.ExamId, StudentId=a.StudentId, AlertType=ProctoringAlertType.SystemAudit,
        CountsAsViolation=false, IsReviewed=true, ModelName="Certificate lifecycle", ActionByUserId=actor,
        Description=decision+": "+reason[..Math.Min(reason.Length,450)],
        MachineObservationsJson=JsonSerializer.Serialize(new{decision,reason,actor,atUtc=DateTime.UtcNow}) };
}
