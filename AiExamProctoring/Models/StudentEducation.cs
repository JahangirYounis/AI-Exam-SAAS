using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class StudentEducation
{
    [Key] public int StudentEducationId { get; set; }
    [Required, StringLength(450)] public string StudentId { get; set; } = string.Empty;
    [ForeignKey(nameof(StudentId))] public ApplicationUser? Student { get; set; }
    [Required, StringLength(80)] public string QualificationLevel { get; set; } = string.Empty;
    [Required, StringLength(150)] public string DegreeTitle { get; set; } = string.Empty;
    [Required, StringLength(180)] public string InstituteName { get; set; } = string.Empty;
    [StringLength(180)] public string? BoardUniversity { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    public int? PassingYear { get; set; }
    [StringLength(30)] public string? GradeCgpaDivision { get; set; }
    public decimal? Percentage { get; set; }
    [StringLength(250)] public string? MajorSubjects { get; set; }
    [StringLength(500)] public string? TranscriptPath { get; set; }
    public bool IsCurrent { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
