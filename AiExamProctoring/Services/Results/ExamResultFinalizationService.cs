using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AiExamProctoring.Services.Results;

/// <summary>All writes run under the same attempt lock used by answer submission and proctoring.
/// Static entry points deliberately accept the caller's DbContext so a submit/block transaction stays atomic.</summary>
public static class ExamResultFinalizationService
{
    public const string AuditModel = "Result publication review";

    public static async Task ScoreSessionAsync(ApplicationDbContext db, ExamSessionAttempt session,
        bool shuffleOptions, IDictionary<int, string?>? submitted, DateTime now,
        DateTime? latestSavedAt = null, CancellationToken ct = default)
    {
        var examId = session.Attempt?.ExamId ?? await db.ExamAttempts.Where(a => a.AttemptId == session.AttemptId).Select(a => a.ExamId).SingleAsync(ct);
        var rows = await db.ExamQuestions.Include(q => q.Question)
            .Where(q => q.ExamId == examId && q.SessionId == session.SessionId && q.IsActive).ToListAsync(ct);
        var answers = await db.StudentAnswers.Where(a => a.SessionAttemptId == session.SessionAttemptId).ToDictionaryAsync(a => a.QuestionId, ct);
        session.MarksObtained = 0; session.TotalMarks = rows.Sum(q => q.Marks);
        session.CorrectAnswers = session.WrongAnswers = session.SkippedAnswers = 0;
        foreach (var row in rows)
        {
            answers.TryGetValue(row.QuestionId, out var answer);
            var supplied = submitted != null && submitted.TryGetValue(row.QuestionId, out var value) ? value : answer?.SelectedOption;
            var selected = supplied?.Trim().ToUpperInvariant();
            if (latestSavedAt.HasValue && answer?.AnsweredAt > latestSavedAt.Value) selected = null;
            if (string.IsNullOrWhiteSpace(selected)) selected = null;
            if (selected != null && (selected.Length != 1 || !"ABCDE".Contains(selected) || selected == "E" && string.IsNullOrWhiteSpace(row.Question?.OptionE)))
                throw new InvalidOperationException("Invalid answer option.");
            if (answer == null)
            {
                answer = new StudentAnswer { SessionAttemptId = session.SessionAttemptId, QuestionId = row.QuestionId, AnsweredAt = now };
                db.StudentAnswers.Add(answer);
            }
            // Timeout scoring must never overwrite the original saved selection/timestamp.
            if (submitted != null && submitted.ContainsKey(row.QuestionId)) { answer.SelectedOption = selected; answer.AnsweredAt = now; }
            var correct = row.Question?.CorrectAnswer;
            if (shuffleOptions && row.Question != null)
            {
                var options = new List<string> { "A", "B", "C", "D" };
                if (!string.IsNullOrWhiteSpace(row.Question.OptionE)) options.Add("E");
                var random = new Random(unchecked(2719 + session.SessionAttemptId * 47 + row.QuestionId));
                for (var i = options.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (options[i], options[j]) = (options[j], options[i]); }
                var index = options.IndexOf(correct!);
                if (index >= 0) correct = "ABCDE"[index].ToString();
            }
            answer.IsCorrect = selected == null ? null : string.Equals(selected, correct, StringComparison.OrdinalIgnoreCase);
            answer.MarksAwarded = selected == null ? 0 : answer.IsCorrect == true ? row.Marks : -row.NegativeMarks;
            session.MarksObtained += answer.MarksAwarded;
            if (selected == null) session.SkippedAnswers++; else if (answer.IsCorrect == true) session.CorrectAnswers++; else session.WrongAnswers++;
        }
    }

    public static IQueryable<ProctoringLog> PendingIntegrity(ApplicationDbContext db, int attemptId) => IntegrityIncidents(db, attemptId).Where(l => !l.IsReviewed);

    public static IQueryable<ProctoringLog> IntegrityIncidents(ApplicationDbContext db, int attemptId) => db.ProctoringLogs.Where(l =>
        l.AttemptId == attemptId && l.ParentIncidentId == null &&
        l.AlertType != ProctoringAlertType.SystemAudit && l.AlertType != ProctoringAlertType.SessionRecording &&
        l.AlertType != ProctoringAlertType.AiMonitoringUnavailable && l.AlertType != ProctoringAlertType.CameraMonitoringInterrupted &&
        l.AlertType != ProctoringAlertType.CameraAccessLost && l.AlertType != ProctoringAlertType.MicrophoneAccessLost &&
        l.AlertType != ProctoringAlertType.ScreenCaptureUnavailable && l.AlertType != ProctoringAlertType.AudioDetected &&
        l.AlertType != ProctoringAlertType.TalkingDetected && l.AlertType != ProctoringAlertType.ExamBlocked &&
        l.AlertType != ProctoringAlertType.ReviewRequested &&
        (l.CountsAsViolation || l.PolicyPending || l.AlertType == ProctoringAlertType.AnswerPromptSpeech ||
         l.AlertType == ProctoringAlertType.IdentityMismatch || l.AlertType == ProctoringAlertType.ObjectObservation ||
         l.AlertType == ProctoringAlertType.AdditionalPersonObservation));

    public static async Task<T> WriteAsync<T>(ApplicationDbContext db, int id, Func<Task<T>> action, CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Public entry points are transaction boundaries; callers with pending writes use the InTransaction method.
            db.ChangeTracker.Clear();
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            await ProctoringStateService.LockAttemptAsync(db, id, ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return result;
        });
    }

    public static Task<bool> FinalizeCompletedAsync(ApplicationDbContext db, int id, DateTime now, CancellationToken ct = default) =>
        WriteAsync(db, id, () => FinalizeCompletedInTransactionAsync(db, id, now, ct), ct);

    public static async Task<bool> FinalizeCompletedInTransactionAsync(ApplicationDbContext db, int id, DateTime now, CancellationToken ct = default)
    {
        var attempt = await db.ExamAttempts.Include(a => a.Exam).SingleAsync(a => a.AttemptId == id, ct);
        if (attempt.Status is AttemptStatus.ProctoringBlocked or AttemptStatus.Cancelled or AttemptStatus.NotStarted) return false;
        var required = await db.ExamSessions.Where(s => s.ExamId == attempt.ExamId && s.IsActive).Select(s => s.SessionId).ToListAsync(ct);
        var sessions = await db.ExamSessionAttempts.Where(s => s.AttemptId == id).ToListAsync(ct);
        if (required.Count == 0 || required.Any(id => !sessions.Any(s => s.SessionId == id && s.Status is AttemptStatus.Completed or AttemptStatus.AutoSubmitted))) return false;
        if (attempt.Status == AttemptStatus.InProgress) attempt.Status = AttemptStatus.Completed;
        attempt.CompletedAt ??= now;
        await AggregateAsync(db, attempt, sessions, now, ct);
        if (!attempt.ResultReleasedAtUtc.HasValue && !attempt.IsDisqualified && !attempt.SubmittedForViolations &&
            attempt.Status != AttemptStatus.Disqualified &&
            !await db.ProctoringLogs.AnyAsync(l => l.AttemptId == id && l.ModelName == AuditModel && l.Description != null && l.Description.StartsWith("Hold:"), ct) &&
            !await IntegrityIncidents(db, id).AnyAsync(ct))
            Release(db, attempt, null, "Automatic", "Completed without unresolved qualifying integrity incidents.");
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task AggregateAsync(ApplicationDbContext db, ExamAttempt attempt, List<ExamSessionAttempt> sessions, DateTime now, CancellationToken ct)
    {
        // Existing historical results are immutable; never silently regrade them with today's question bank.
        var existing = await db.ExamResults.SingleOrDefaultAsync(r => r.AttemptId == attempt.AttemptId, ct);
        if (existing != null) return;
        attempt.TotalMarksObtained = sessions.Sum(s => s.MarksObtained);
        attempt.TotalMarks = attempt.Exam!.TotalMarks;
        attempt.Percentage = attempt.TotalMarks <= 0 ? 0 : Math.Round(attempt.TotalMarksObtained / attempt.TotalMarks * 100, 2);
        attempt.IsPassed = StudentResultAccess.IsAcademicPass(attempt.Percentage, attempt.Exam.PassingPercentage);
        db.ExamResults.Add(new ExamResult { AttemptId = attempt.AttemptId, TotalMarksObtained = attempt.TotalMarksObtained,
            TotalMarks = attempt.TotalMarks, Percentage = attempt.Percentage, IsPassed = attempt.IsPassed,
            CorrectAnswers = sessions.Sum(s => s.CorrectAnswers), WrongAnswers = sessions.Sum(s => s.WrongAnswers),
            SkippedAnswers = sessions.Sum(s => s.SkippedAnswers), GeneratedDate = now,
            CertificateNumber = $"AIE-{now:yyyyMMdd}-{attempt.AttemptId:000000}" });
    }

    public static Task<string> ReviewAsync(ApplicationDbContext db, int id, string teacherId, string decision, string reason, CancellationToken ct = default) =>
        WriteAsync(db, id, async () =>
        {
            if (decision is not ("Publish" or "Hold") || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 1000)
                throw new ArgumentException("Choose Publish or Hold and provide a reason of 10–1000 characters.");
            var attempt = await db.ExamAttempts.Include(a => a.Exam).SingleOrDefaultAsync(a => a.AttemptId == id && a.Exam!.CreatedBy == teacherId, ct)
                ?? throw new UnauthorizedAccessException();
            if (attempt.ResultReleasedAtUtc.HasValue) return decision == "Publish" ? "Already published" : throw new InvalidOperationException("Published decisions cannot be reversed here.");
            if (attempt.Status is not (AttemptStatus.Completed or AttemptStatus.AutoSubmitted or AttemptStatus.ProctoringBlocked or AttemptStatus.Disqualified))
                throw new PublicationPendingException("Active or unstarted attempts cannot be finalized or published.");
            if (decision == "Hold")
            {
                var last = await db.ProctoringLogs.Where(l => l.AttemptId == id && l.ModelName == AuditModel).OrderByDescending(l => l.LogId).FirstOrDefaultAsync(ct);
                if (last?.Description != "Hold: " + reason.Trim()[..Math.Min(reason.Trim().Length, 450)]) Audit(db, attempt, teacherId, decision, reason);
                return "Held for review";
            }
            if (await PendingIntegrity(db, id).AnyAsync(ct)) throw new PublicationPendingException("Resolve the qualifying evidence incidents before publishing. Publication does not approve evidence.");
            var sessions = await db.ExamSessionAttempts.Where(s => s.AttemptId == id).ToListAsync(ct);
            if (!await db.ExamResults.AnyAsync(r => r.AttemptId == id, ct))
            {
                foreach (var session in sessions)
                    await ScoreSessionAsync(db, session, attempt.Exam!.ShuffleOptions, null, DateTime.Now, ct: ct);
                await AggregateAsync(db, attempt, sessions, DateTime.Now, ct);
            }
            // Finalizing a blocked attempt preserves its blocked state and saved answers.
            attempt.CompletedAt ??= DateTime.Now;
            Release(db, attempt, teacherId, decision, reason);
            return "Published";
        }, ct);

    public static async Task<int> PublishEligibleAsync(ApplicationDbContext db, int examId, string teacherId, string reason, CancellationToken ct = default)
    {
        if (!await db.Exams.AnyAsync(e => e.ExamId == examId && e.CreatedBy == teacherId, ct)) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 1000) throw new ArgumentException("Provide a publication reason of 10–1000 characters.");
        var ids = await db.ExamAttempts.Where(a => a.ExamId == examId && a.ResultReleasedAtUtc == null &&
            (a.Status == AttemptStatus.Completed || a.Status == AttemptStatus.AutoSubmitted)).Select(a => a.AttemptId).ToListAsync(ct);
        var released = 0;
        foreach (var id in ids)
        {
            // Eligibility is rechecked inside the same serialized transaction as publication.
            try { if (await ReviewAsync(db, id, teacherId, "Publish", reason, ct) == "Published") released++; }
            catch (PublicationPendingException) { /* Eligibility changed or evidence remains unresolved. */ }
        }
        return released;
    }

    private static void Release(ApplicationDbContext db, ExamAttempt attempt, string? actor, string decision, string reason)
    {
        attempt.ResultReleasedAtUtc = DateTime.UtcNow; attempt.ResultReviewedBy = actor;
        Audit(db, attempt, actor, decision, reason);
        db.StudentNotifications.Add(new StudentNotification { StudentId = attempt.StudentId, ExamId = attempt.ExamId,
            Type = "ResultPublished:" + attempt.AttemptId, Title = "Your examination result is available",
            Message = "Your earned marks are available in My Results. Academic marks are separate from integrity review.", CreatedDate = DateTime.Now });
    }
    private static void Audit(ApplicationDbContext db, ExamAttempt attempt, string? actor, string decision, string reason) =>
        db.ProctoringLogs.Add(new ProctoringLog { AttemptId = attempt.AttemptId, ExamId = attempt.ExamId, StudentId = attempt.StudentId,
            AlertType = ProctoringAlertType.SystemAudit, ModelName = AuditModel, CountsAsViolation = false, IsReviewed = true,
            ActionByUserId = actor, Description = decision + ": " + reason.Trim()[..Math.Min(reason.Trim().Length, 450)],
            MachineObservationsJson = JsonSerializer.Serialize(new { decision, reason = reason.Trim(), reviewerId = actor,
                reviewedAtUtc = DateTime.UtcNow, releasedAtUtc = attempt.ResultReleasedAtUtc }), CreatedDate = DateTime.Now });
}

public sealed class PublicationPendingException(string message) : InvalidOperationException(message);
