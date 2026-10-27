using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherDashboardViewModel
{
    public int TotalExams { get; init; }
    public int DraftExams { get; init; }
    public int PublishedExams { get; init; }
    public int RunningExams { get; init; }
    public int PrivateExams { get; init; }
    public int PublicExams { get; init; }
    public int AssignedStudents { get; init; }
    public int Attempts { get; init; }
    public int Results { get; init; }
    public int PendingAlerts { get; init; }
    public int TotalQuestions { get; init; }
    public int SharedApprovedQuestions { get; init; }
    public int AvailableQuestionCount => TotalQuestions + SharedApprovedQuestions;
    public int BlockedAttempts { get; init; }
    public int TodayExams { get; init; }
    public List<Exam> RecentExams { get; init; } = new();
    public List<Exam> UpcomingExams { get; init; } = new();
}
