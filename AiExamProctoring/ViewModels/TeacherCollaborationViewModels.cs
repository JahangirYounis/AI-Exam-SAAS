using AiExamProctoring.Services.Collaboration;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherCollaborationEventViewModel
{
    public string EventType { get; init; } = string.Empty;
    public string ActorName { get; init; } = "Teacher";
    public DateTime OccurredAtUtc { get; init; }
    public bool CanEditQuestions { get; init; }
    public bool CanMonitor { get; init; }
}

public sealed class TeacherCollaborationCardViewModel
{
    public int InvitationId { get; init; }
    public int ExamId { get; init; }
    public string ExamTitle { get; init; } = "Examination";
    public string OwnerName { get; init; } = "Exam owner";
    public CollaborationState Status { get; init; }
    public bool CanEditQuestions { get; init; }
    public bool CanMonitor { get; init; }
    public bool IsHistoricalCycle { get; init; }
    public bool IsReusable { get; init; }
    public DateTime InvitedAtUtc { get; init; }
    public DateTime? AcceptedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public DateTime? ExamStartLocal { get; init; }
    public List<TeacherCollaborationEventViewModel> History { get; init; } = new();
}

public sealed class TeacherCollaborationPageViewModel
{
    public List<TeacherCollaborationCardViewModel> Pending { get; init; } = new();
    public List<TeacherCollaborationCardViewModel> Active { get; init; } = new();
    public List<TeacherCollaborationCardViewModel> Past { get; init; } = new();
}
