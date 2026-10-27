using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class StudentCertification
{
    [Key] public int StudentCertificationId { get; set; }
    [Required, StringLength(450)] public string StudentId { get; set; } = string.Empty;
    [ForeignKey(nameof(StudentId))] public ApplicationUser? Student { get; set; }
    [Required, StringLength(150)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(180)] public string InstituteName { get; set; } = string.Empty;
    [StringLength(80)] public string? Duration { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    [StringLength(500)] public string? CertificatePath { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
