using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class StudentNotification
{
    [Key]
    public int StudentNotificationId { get; set; }

    [Required, StringLength(450)]
    public string StudentId { get; set; } = string.Empty;
    [ForeignKey(nameof(StudentId))]
    public virtual ApplicationUser? Student { get; set; }

    public int? ExamId { get; set; }
    [ForeignKey(nameof(ExamId))]
    public virtual Exam? Exam { get; set; }

    [Required, StringLength(150)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(700)] public string Message { get; set; } = string.Empty;
    [StringLength(80)] public string Type { get; set; } = "Information";
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? ReadDate { get; set; }
    public bool IsRead { get; set; }
}
