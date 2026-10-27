using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class StudentExperience
{
    [Key] public int StudentExperienceId { get; set; }
    [Required, StringLength(450)] public string StudentId { get; set; } = string.Empty;
    [ForeignKey(nameof(StudentId))] public ApplicationUser? Student { get; set; }
    [Required, StringLength(150)] public string OrganizationName { get; set; } = string.Empty;
    [Required, StringLength(120)] public string JobTitle { get; set; } = string.Empty;
    [StringLength(80)] public string? EmploymentType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsCurrentlyWorking { get; set; }
    [StringLength(700)] public string? Description { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
