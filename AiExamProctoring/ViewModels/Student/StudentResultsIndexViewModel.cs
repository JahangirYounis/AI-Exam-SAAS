using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels.Student;

public sealed class StudentResultsIndexViewModel
{
    public List<ExamResult> ReleasedResults { get; init; } = new();
    public List<PendingStudentResultRow> PendingAttempts { get; init; } = new();
    public int ReleasedTotal { get; init; }
    public int PendingTotal { get; init; }
    public int Page { get; init; } = 1;
    public int PendingPage { get; init; } = 1;
    public int ReleasedPages { get; init; } = 1;
    public int PendingPages { get; init; } = 1;
}

public sealed class PendingStudentResultRow
{
    public int AttemptId { get; init; }
    public string ExamTitle { get; init; } = string.Empty;
    public DateTime ExaminationDate { get; init; }
    public bool IsBlocked { get; init; }
    public bool NeedsTeacherReview { get; init; }
}
