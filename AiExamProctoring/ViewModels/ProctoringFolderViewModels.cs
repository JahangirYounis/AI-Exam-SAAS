using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

/// <summary>
/// Candidate-first review structure used by Admin governance and Teacher-owned
/// monitoring. It mirrors App_Data/ProctoringEvidence/candidate-*/exam-*.
/// </summary>
public sealed class CandidateProctoringFolderViewModel
{
    public string StudentId { get; init; } = string.Empty;
    public string CandidateName { get; init; } = "Unknown candidate";
    public string CandidateNumber { get; init; } = "Not assigned";
    public string Email { get; init; } = string.Empty;
    public string? ProfileImage { get; init; }
    public int ExamFolderCount { get; init; }
    public int AlertCount { get; init; }
    public int ViolationCount { get; init; }
    public int PendingCount { get; init; }
    public int EvidenceCount { get; init; }
    public int HighSeverityCount { get; init; }
    public DateTime? LatestEventAt { get; init; }
}

public sealed class ExamProctoringFolderViewModel
{
    public int ExamId { get; init; }
    public string ExamTitle { get; init; } = "Unknown examination";
    public string TeacherName { get; init; } = "Unknown teacher";
    public ExamAudience Audience { get; init; }
    public ExamPurpose Purpose { get; init; }
    public int AttemptCount { get; init; }
    public int AlertCount { get; init; }
    public int ViolationCount { get; init; }
    public int PendingCount { get; init; }
    public int EvidenceCount { get; init; }
    public int HighSeverityCount { get; init; }
    public DateTime? LatestEventAt { get; init; }
}

public sealed class ProctoringCandidateFolderPageViewModel
{
    public CandidateProctoringFolderViewModel Candidate { get; init; } = new();
    public IReadOnlyList<ExamProctoringFolderViewModel> ExamFolders { get; init; }
        = Array.Empty<ExamProctoringFolderViewModel>();
    public bool TeacherScope { get; init; }
}

public sealed class ProctoringExamFolderPageViewModel
{
    public IReadOnlyList<int> BlockedAttemptIds { get; init; } = Array.Empty<int>();
    public CandidateProctoringFolderViewModel Candidate { get; init; } = new();
    public ExamProctoringFolderViewModel ExamFolder { get; init; } = new();
    public IReadOnlyList<ProctoringLog> Logs { get; init; } = Array.Empty<ProctoringLog>();
    public bool TeacherScope { get; init; }
    public bool PendingOnly { get; init; }
}
