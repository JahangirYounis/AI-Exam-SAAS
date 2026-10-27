using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class ExamAssignment
{
    [Key]
    public int ExamAssignmentId { get; set; }

    [Required]
    public int ExamId { get; set; }
    [ForeignKey(nameof(ExamId))]
    public virtual Exam? Exam { get; set; }

    [Required, StringLength(450)]
    public string StudentId { get; set; } = string.Empty;
    [ForeignKey(nameof(StudentId))]
    public virtual ApplicationUser? Student { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.Now;
    [StringLength(450)] public string? AssignedBy { get; set; }
    [StringLength(40)]
    public string? RollNumber { get; set; }

    public bool IsActive { get; set; } = true;
}
