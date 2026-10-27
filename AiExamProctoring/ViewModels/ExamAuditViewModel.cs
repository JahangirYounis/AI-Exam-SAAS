using AiExamProctoring.Models;
namespace AiExamProctoring.ViewModels;
public sealed class ExamAuditViewModel
{
    public string Creator { get; init; } = "Unknown";
    public DateTime? CreatorFirstMonitoringAtUtc { get; init; }
    public DateTime? CreatorLastMonitoringAtUtc { get; init; }
    public List<string> QuestionAuthors { get; init; } = [];
    public int Selected { get; init; }
    public List<ExamInvigilator> Team { get; init; } = [];
    public List<ExamAttempt> Attempts { get; init; } = [];
    public List<ProctoringLog> Events { get; init; } = [];
}
