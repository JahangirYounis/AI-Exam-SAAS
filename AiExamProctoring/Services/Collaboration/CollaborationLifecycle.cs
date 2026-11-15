
using AiExamProctoring.Models;
using ExamModel = AiExamProctoring.Models.Exam;

namespace AiExamProctoring.Services.Collaboration;

public enum CollaborationState
{
    Pending,
    Active,
    Completed,
    Cancelled,
    Expired,
    Revoked,
    Declined,
    Inactive
}

public static class CollaborationLifecycle
{
    // EndDate is a [NotMapped] property.
    // Load Exam.Sessions before classification.
    public static bool IsExamPast(
        ExamModel? exam,
        DateTime localNow)
    {
        return exam is null ||
               exam.Status is ExamStatus.Completed
                   or ExamStatus.Cancelled ||
               (exam.IsPublished &&
                !exam.IsReusable &&
                localNow >= exam.EndDate);
    }

    public static bool IsDuplicateInvitation(
        ExamInvigilator? row)
    {
        return row is not null &&
               row.RevokedAtUtc is null;
    }

    public static bool CanAccess(
        ExamInvigilator invitation,
        bool forQuestions = false,
        bool forMonitoring = false)
    {
        return invitation.IsActive &&
               invitation.AcceptedAtUtc.HasValue &&
               invitation.RevokedAtUtc is null &&
               (invitation.CanEditQuestions ||
                invitation.CanMonitor) &&
               (!forQuestions ||
                invitation.CanEditQuestions) &&
               (!forMonitoring ||
                invitation.CanMonitor);
    }

    public static CollaborationState Classify(
        ExamInvigilator invitation,
        DateTime localNow,
        bool declined = false)
    {
        if (invitation.RevokedAtUtc.HasValue)
            return declined
                ? CollaborationState.Declined
                : CollaborationState.Revoked;

        if (invitation.Exam?.Status == ExamStatus.Cancelled)
            return CollaborationState.Cancelled;

        if (invitation.Exam?.Status == ExamStatus.Completed)
            return CollaborationState.Completed;

        if (IsExamPast(invitation.Exam, localNow))
            return CollaborationState.Expired;

        if (CanAccess(invitation))
            return CollaborationState.Active;

        if (invitation.AcceptedAtUtc.HasValue)
            return CollaborationState.Inactive;

        return CollaborationState.Pending;
    }

    public static bool IsPast(CollaborationState value)
    {
        return value is not (
            CollaborationState.Pending or
            CollaborationState.Active
        );
    }
}
