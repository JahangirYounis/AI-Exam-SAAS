using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels.Student;

public sealed class SessionBreakViewModel
{
    public int AttemptId { get; set; }
    public string ExamTitle { get; set; } = string.Empty;
    public int CompletedSessionNumber { get; set; }
    public int TotalSessions { get; set; }
    public int NextSessionNumber { get; set; }
    public string NextSessionTitle { get; set; } = string.Empty;
    public int NextSessionDurationMinutes { get; set; }
    public decimal NextSessionTotalMarks { get; set; }
    public DateTime ServerNow { get; set; } = DateTime.Now;
    public DateTime RestEndsAt { get; set; }
    public int MaximumRestMinutes { get; set; }
}
