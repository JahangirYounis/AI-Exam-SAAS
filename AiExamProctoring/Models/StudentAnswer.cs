using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class StudentAnswer
{
    [Key] public int AnswerId { get; set; }

    [Required] public int SessionAttemptId { get; set; }
    [ForeignKey(nameof(SessionAttemptId))] public virtual ExamSessionAttempt? SessionAttempt { get; set; }

    [Required] public int QuestionId { get; set; }
    [ForeignKey(nameof(QuestionId))] public virtual Question? Question { get; set; }

    [StringLength(1), RegularExpression("^[ABCDE]$")]
    public string? SelectedOption { get; set; }

    public bool? IsCorrect { get; set; }
    [Column(TypeName="decimal(8,2)")] public decimal MarksAwarded { get; set; }
    public DateTime AnsweredAt { get; set; } = DateTime.Now;
    public bool IsMarkedForReview { get; set; }

    [NotMapped] public DateTime AnsweredDate { get => AnsweredAt; set => AnsweredAt = value; }
    [NotMapped] public bool IsSkipped => string.IsNullOrWhiteSpace(SelectedOption);
}
