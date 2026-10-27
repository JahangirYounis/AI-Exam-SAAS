namespace AiExamProctoring.ViewModels.Student
{
    public class QuestionReviewViewModel
    {
        public int QuestionId { get; set; }

        public string QuestionText { get; set; } = string.Empty;

        public string OptionA { get; set; } = string.Empty;

        public string OptionB { get; set; } = string.Empty;

        public string OptionC { get; set; } = string.Empty;

        public string OptionD { get; set; } = string.Empty;

        public string CorrectAnswer { get; set; } = string.Empty;

        public string? StudentAnswer { get; set; }

        public bool IsCorrect { get; set; }

        public decimal MarksAwarded { get; set; }

        public decimal QuestionMarks { get; set; }

        //----------------------------------------------------
        // Future Ready
        //----------------------------------------------------

        public string? Explanation { get; set; }
    }
}