using System.ComponentModel.DataAnnotations;
namespace AiExamProctoring.Models;
public sealed class ExamTeamMessage {
 public int Id { get; set; }
 public int ExamId { get; set; }
 public Exam? Exam { get; set; }
 [Required, MaxLength(450)] public string SenderId { get; set; } = "";
 [Required, MaxLength(2000)] public string Body { get; set; } = "";
 public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
