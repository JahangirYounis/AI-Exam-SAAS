using AiExamProctoring.Models;

namespace AiExamProctoring.Services.Proctoring;

public enum ReviewRequestStatus { Pending, Approved, Rejected, Completed }

/// <summary>Deterministic display and transition guards for a request linked to an existing audit log.</summary>
public static class ReviewRequestRules
{
    public const string RequestModel = "Student appeal";
    public const string DecisionModel = "Teacher appeal decision";

    public static ReviewRequestStatus Resolve(AttemptStatus attemptStatus, bool finalized,
        string? decision, bool belongsToCurrentLock)
    {
        if (finalized || attemptStatus is AttemptStatus.Completed or AttemptStatus.AutoSubmitted
            or AttemptStatus.Cancelled or AttemptStatus.Disqualified)
            return ReviewRequestStatus.Completed;
        if (decision == "approved") return ReviewRequestStatus.Approved;
        if (decision == "rejected") return ReviewRequestStatus.Rejected;
        return belongsToCurrentLock && attemptStatus == AttemptStatus.ProctoringBlocked
            ? ReviewRequestStatus.Pending : ReviewRequestStatus.Completed;
    }

    // The lock's ExamBlocked log is stable even if other services increment StateVersion.
    // Old saved "lock:<version>" requests remain tied to their original block epoch.
    public static bool BelongsToCurrentLock(int requestId, int lastBlockLogId,
        string? requestModelVersion, long currentVersion) =>
        requestModelVersion?.StartsWith("lock:", StringComparison.Ordinal) == true &&
        (lastBlockLogId > 0 ? requestId > lastBlockLogId
            : requestModelVersion == $"lock:{currentVersion}");

    public static bool CanResolve(ExamAttempt attempt, bool hasFinalResult) =>
        attempt.Status == AttemptStatus.ProctoringBlocked &&
        !attempt.CompletedAt.HasValue && !attempt.ResultReleasedAtUtc.HasValue &&
        !attempt.IsDisqualified && !hasFinalResult &&
        attempt.Exam is not null && attempt.Exam.Status is not (ExamStatus.Completed or ExamStatus.Cancelled);

    public static bool IsValidReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length is >= 10 and <= 300;
}
