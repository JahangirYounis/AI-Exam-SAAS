namespace AiExamProctoring.ViewModels.Student
{
    public class StudentNotificationViewModel
    {
        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public DateTime Date { get; set; }

        public bool IsRead { get; set; }

        // Future Ready

        public string Icon { get; set; } = "bi-bell-fill";

        public string BadgeColor { get; set; } = "primary";

        public string Url { get; set; } = "#";
    }
}