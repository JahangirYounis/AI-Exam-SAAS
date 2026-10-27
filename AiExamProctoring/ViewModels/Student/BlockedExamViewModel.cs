namespace AiExamProctoring.ViewModels.Student;

public sealed class BlockedExamViewModel
{
    public int AttemptId { get; init; }
    public string StudentName { get; init; } = "Candidate";
    public string ExamTitle { get; init; } = "Examination";
    public string Reason { get; init; } = "Instructor review is required.";
    public int FrozenSeconds { get; init; }
    public int SavedAnswers { get; init; }
    public DateTime? LockedAt { get; init; }
    public bool ReviewRequested { get; init; }
    public IReadOnlyList<BlockedExamIncident> Incidents { get; init; } = [];
    public string FrozenTime => TimeSpan.FromSeconds(Math.Max(0, FrozenSeconds)).ToString(@"hh\:mm\:ss");
}

public sealed record BlockedExamIncident(int Id, DateTime Timestamp, string Stage, string Category,
    string Description, string? ScreenshotUrl, string? AudioUrl, string? VideoUrl, bool FinalAction, string? Transcript = null, decimal? Similarity = null, int? MatchedQuestionId = null);
