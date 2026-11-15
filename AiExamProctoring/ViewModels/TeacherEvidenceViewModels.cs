using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherEvidenceIndexViewModel
{
    public IReadOnlyList<TeacherEvidenceExamRow> Exams { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int TotalExams { get; init; }
    public int TotalCandidates { get; init; }
    public int PendingStudents { get; init; }
    public int BlockedStudents { get; init; }
}

public sealed class TeacherEvidenceExamRow
{
    public int ExamId { get; init; }
    public string Title { get; init; } = "Examination";
    public DateTime ExamDate { get; init; }
    public bool IsOwner { get; init; }
    public bool IsReusable { get; init; }
    public ExamStatus Status { get; init; }
    public int StudentsTested { get; init; }
    public int BlockedStudents { get; init; }
    public int FlaggedStudents { get; init; }
    public int PendingStudents { get; init; }
    public int TotalAttempts { get; init; }
}

public sealed class TeacherEvidenceStudentRow
{
    public string StudentId { get; init; } = "";
    public string Name { get; init; } = "Candidate";
    public string? CandidateNumber { get; init; }
    public string? Email { get; init; }
    public int AttemptCount { get; init; }
    public int BlockedAttemptCount { get; init; }
    public int PendingEvents { get; init; }
    public int ReviewEvents { get; init; }
    public int EvidenceFiles { get; init; }
    public DateTime LatestAt { get; init; }
    public bool IsBlocked => BlockedAttemptCount > 0;
    public bool IsReviewed => ReviewEvents > 0 && PendingEvents == 0 && !IsBlocked;
}

public sealed class TeacherEvidenceExamViewModel
{
    public int ExamId { get; init; }
    public string ExamTitle { get; init; } = "Examination";
    public bool IsOwner { get; init; }
    public string? Search { get; init; }
    public string Category { get; init; } = "all";
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int TotalStudents { get; init; }
    public int BlockedCount { get; init; }
    public int FlaggedCount { get; init; }
    public int PendingCount { get; init; }
    public IReadOnlyList<TeacherEvidenceStudentRow> Students { get; init; } = [];
}

public sealed class TeacherEvidenceAttemptRow
{
    public int AttemptId { get; init; }
    public AttemptStatus Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int ReviewEvents { get; init; }
    public int PendingEvents { get; init; }
    public int EvidenceFiles { get; init; }
}

public sealed class TeacherEvidenceCandidateViewModel
{
    public int ExamId { get; init; }
    public string ExamTitle { get; init; } = "Examination";
    public string StudentId { get; init; } = "";
    public string StudentName { get; init; } = "Candidate";
    public string? CandidateNumber { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<TeacherEvidenceAttemptRow> Attempts { get; init; } = [];
}
