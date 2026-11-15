using AiExamProctoring.Models;

namespace AiExamProctoring.Services.Exam;

/// <summary>
/// UI eligibility only. Authoritative exam/session access is rechecked by ExamController.
/// Never treat blocked, finalized or expired sessions as resumable.
/// </summary>
public static class StudentPortalAttemptState
{
    public static bool IsHeld(ExamAttempt attempt) =>
        attempt.Status == AttemptStatus.ProctoringBlocked &&
        !attempt.ResultReleasedAtUtc.HasValue;

    public static bool CanOfferResume(ExamAttempt attempt, DateTime localNow)
    {
        if (attempt.Status != AttemptStatus.InProgress || attempt.CompletedAt.HasValue ||
            attempt.ResultReleasedAtUtc.HasValue || attempt.IsDisqualified || attempt.Exam is null ||
            attempt.Exam.Status is ExamStatus.Cancelled or ExamStatus.Completed)
            return false;

        // A timed-out active session is finalized by ExamController, not resumed by the UI.
        // Do not infer whole-exam expiry from the entry window: started sessions have their own clock.
        foreach (var session in attempt.SessionAttempts.Where(s => s.Status == AttemptStatus.InProgress))
        {
            if (session.Session is null || !session.StartedAt.HasValue) return false;
            if (localNow >= session.StartedAt.Value.AddMinutes(session.Session.DurationMinutes)) return false;
        }
        return true;
    }
}
