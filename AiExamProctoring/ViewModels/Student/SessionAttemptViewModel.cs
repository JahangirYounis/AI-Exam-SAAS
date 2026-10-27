using AiExamProctoring.Models;

namespace AiExamProctoring.ViewModels.Student;

public class SessionAttemptViewModel
{
    public ExamSessionAttempt SessionAttempt { get; set; } = null!;
    public Exam Exam { get; set; } = null!;
    public ExamSession Session { get; set; } = null!;
    public List<Question> Questions { get; set; } = new();
    public Dictionary<int,string?> ExistingAnswers { get; set; } = new();
    public HashSet<int> ReviewedQuestionIds { get; set; } = new();
    public int SessionIndex { get; set; }
    public int TotalSessions { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime ServerNow { get; set; }
}
