using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels;

public sealed class AdminDashboardViewModel
{
    // Core counts
    public int TotalExams { get; init; }
    public int PublishedExams { get; init; }
    public int ActiveExams { get; init; }
    public int TotalQuestions { get; init; }
    public int VerifiedQuestions { get; init; }
    public int TotalStudents { get; init; }
    public int PendingApproval { get; init; }
    public int ActiveStudents { get; init; }
    public int BlockedStudents { get; init; }
    public int TotalTeachers { get; init; }
    public int PendingTeachers { get; init; }
    public int ActiveTeachers { get; init; }
    public int TotalAlerts { get; init; }
    public int PendingAlerts { get; init; }

    // Master data
    public int TotalDepartments { get; init; }
    public int ActiveDepartments { get; init; }
    public int TotalPosts { get; init; }
    public int ActivePosts { get; init; }
    public int TotalSubjects { get; init; }
    public int ActiveSubjects { get; init; }
    public int LockedSubjects { get; init; }

    // Exam pipeline
    public int DraftExams { get; init; }
    public int BuildingExams { get; init; }
    public int ReadyExams { get; init; }
    public int ScheduledExams { get; init; }
    public int RunningExams { get; init; }
    public int CompletedExams { get; init; }
    public int CancelledExams { get; init; }
    public int LockedExams { get; init; }
    public int ReadyForScheduling { get; init; }
    public int IncompleteExams { get; init; }

    // Operations
    public int TotalSessions { get; init; }
    public int ActiveSessions { get; init; }
    public int TotalAttempts { get; init; }
    public int TotalResults { get; init; }
    public int PassedResults { get; init; }
    public int FailedResults { get; init; }

    // Dashboard lists
    public List<Exam> RecentExams { get; init; } = new();
    public List<Exam> UpcomingExams { get; init; } = new();
    public List<Question> RecentQuestions { get; init; } = new();
    public List<ProctoringLog> RecentAlerts { get; init; } = new();

    public int QuestionVerificationPercentage =>
        TotalQuestions == 0
            ? 0
            : (int)Math.Round(VerifiedQuestions * 100d / TotalQuestions);

    public int ExamReadinessPercentage =>
        TotalExams == 0
            ? 0
            : (int)Math.Round(ReadyForScheduling * 100d / TotalExams);

    public int PassPercentage =>
        TotalResults == 0
            ? 0
            : (int)Math.Round(PassedResults * 100d / TotalResults);
}
