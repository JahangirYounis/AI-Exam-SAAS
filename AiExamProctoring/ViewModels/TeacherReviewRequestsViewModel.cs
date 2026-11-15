using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherReviewRequestItemViewModel
{
    public int RequestId { get; init; }
    public int AttemptId { get; init; }
    public int ExamId { get; init; }
    public string StudentId { get; init; } = "";
    public string StudentName { get; init; } = "Candidate";
    public string? CandidateNumber { get; init; }
    public string ExamTitle { get; init; } = "Examination";
    public AttemptStatus AttemptStatus { get; init; }
    public ReviewRequestStatus Status { get; init; }
    public DateTime RequestedAt { get; init; }
    public string BlockReason { get; init; } = "Review of the proctoring evidence requested.";
    public string? DecisionReason { get; init; }
    public string? ReviewerName { get; init; }
    public DateTime? DecidedAt { get; init; }
    public int IncidentCount { get; init; }
    public int EvidenceCount { get; init; }
    public bool CanDecide { get; init; }
}

public sealed class TeacherReviewRequestsViewModel
{
    public string Filter { get; init; } = "pending";
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int TotalMatching { get; init; }
    public int Pending { get; init; }
    public int Approved { get; init; }
    public int Rejected { get; init; }
    public int Completed { get; init; }
    public IReadOnlyList<TeacherReviewRequestItemViewModel> Requests { get; init; } = [];
}

public sealed class TeacherReviewRequestDetailsViewModel
{
    public TeacherReviewRequestItemViewModel Request { get; init; } = new();
    public IReadOnlyList<ProctoringLog> Incidents { get; init; } = [];
    public IReadOnlyList<ProctoringLog> Decisions { get; init; } = [];
    public int TotalIncidents { get; init; }
}
