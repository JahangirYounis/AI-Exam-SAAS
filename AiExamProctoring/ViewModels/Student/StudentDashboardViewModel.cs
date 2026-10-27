using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels.Student
{
    public class StudentDashboardViewModel
    {
        // ======================================================
        // Logged-in Student
        // ======================================================

        public string StudentId { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;

        public string? ProfileImage { get; set; }

        public string? Department { get; set; }

        public string? RollNumber { get; set; }

        public string? Batch { get; set; }

        public int ProfileCompletionPercentage { get; set; }

        public bool HasIdentityPhoto { get; set; }

        // ======================================================
        // Dashboard Statistics
        // ======================================================

        public int AvailableExamCount { get; set; }

        public int CompletedExamCount { get; set; }

        public int OngoingExamCount { get; set; }

        public int UpcomingExamCount { get; set; }

        public int PassedExamCount { get; set; }

        public int FailedExamCount { get; set; }

        public int TotalAttempts { get; set; }

        public int TotalExams { get; set; }

        public double PassPercentage { get; set; }

        // ======================================================
        // Performance Analytics
        // ======================================================

        public double AverageScore { get; set; }

        public double AccuracyPercentage { get; set; }

        public int TotalQuestionsAttempted { get; set; }

        public int CorrectAnswers { get; set; }

        public int WrongAnswers { get; set; }

        public int SkippedQuestions { get; set; }

        // ======================================================
        // Greeting
        // ======================================================

        public string Greeting { get; set; } = string.Empty;

        public DateTime CurrentDate { get; set; }

        public DateTime? LastLogin { get; set; }

        public DateTime MemberSince { get; set; }

        // ======================================================
        // Exam Lists
        // ======================================================

        public List<Exam> AvailableExams { get; set; } = new();

        public List<Exam> UpcomingExams { get; set; } = new();

        public Exam? NextAvailableExam => AvailableExams.FirstOrDefault();

        public Exam? NextUpcomingExam => UpcomingExams.FirstOrDefault();

        public List<ExamAttempt> OngoingAttempts { get; set; } = new();

        public List<ExamAttempt> RecentAttempts { get; set; } = new();

        public List<ExamResult> RecentResults { get; set; } = new();

        // ======================================================
        // Notifications
        // ======================================================

        public List<StudentNotificationViewModel> Notifications { get; set; }
            = new();

        public int UnreadNotificationCount { get; set; }

        // ======================================================
        // Recent Activities
        // ======================================================

        public List<StudentActivityViewModel> Activities { get; set; }
            = new();
    }
}
