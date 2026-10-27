namespace AiExamProctoring.ViewModels.Student
{
    public class ResultDetailsViewModel
    {
        //=========================================================
        // Student Information
        //=========================================================

        public string StudentName { get; set; } = string.Empty;

        public string? RollNumber { get; set; }

        public string? RegistrationNumber { get; set; }

        public string? Department { get; set; }

        public string? Batch { get; set; }

        public string? Semester { get; set; }

        public string? ProfileImage { get; set; }

        //=========================================================
        // Result Information
        //=========================================================

        public int ResultId { get; set; }

        public int AttemptId { get; set; }

        public bool IsPassed { get; set; }

        public DateTime ExamDate { get; set; }

        //=========================================================
        // Exam Information
        //=========================================================

        public string ExamTitle { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public int Duration { get; set; }

        public int TotalQuestions { get; set; }

        public decimal TotalMarks { get; set; }

        public decimal ObtainedMarks { get; set; }

        public decimal PassingMarks { get; set; }

        //=========================================================
        // Attempt Information
        //=========================================================

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public TimeSpan TotalTimeTaken { get; set; }

        public string Status { get; set; } = string.Empty;

        //=========================================================
        // Performance Analytics
        //=========================================================

        public double Percentage { get; set; }

        public double Accuracy { get; set; }

        public int CorrectAnswers { get; set; }

        public int WrongAnswers { get; set; }

        public int SkippedQuestions { get; set; }

        //=========================================================
        // Question Review
        //=========================================================

        public List<QuestionReviewViewModel> Questions { get; set; }
            = new();
    }
}