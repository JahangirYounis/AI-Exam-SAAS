using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

/// <summary>
/// Presentation-only model for the teacher's own examination listing.
/// Does not change exam persistence, scheduling or publication rules.
/// </summary>
public sealed class TeacherExamIndexViewModel
{
    public string ActiveTab { get; init; } = "all";
    public string Search { get; init; } = string.Empty;
    public string Sort { get; init; } = "recommended";
    public int AllCount { get; init; }
    public int CurrentCount { get; init; }
    public int ScheduledCount { get; init; }
    public int DraftCount { get; init; }
    public int PastCount { get; init; }
    public int MatchedCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 8;
    public int PageCount => Math.Max(1, (MatchedCount + PageSize - 1) / PageSize);
    public List<TeacherExamListItem> Exams { get; init; } = new();
}

public sealed class TeacherExamListItem
{
    public required Exam Exam { get; init; }
    public required string Group { get; init; }
    public required string StatusLabel { get; init; }
    public required string BadgeClass { get; init; }
    public bool CanEdit { get; init; }
    public bool CanManageCandidates { get; init; }
    public bool CanMonitor { get; init; }
    public bool CanViewResults { get; init; }
}
