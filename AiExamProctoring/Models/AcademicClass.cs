using System.ComponentModel.DataAnnotations;
namespace AiExamProctoring.Models;
public sealed class AcademicClass {
 public int Id { get; set; }
 [Required, StringLength(120)] public string Name { get; set; } = "";
 [Required, StringLength(450)] public string TeacherId { get; set; } = "";
 public bool IsActive { get; set; } = true;
 public ICollection<ClassMembership> Members { get; set; } = new List<ClassMembership>();
}
public sealed class ClassMembership {
 public int Id { get; set; }
 public int AcademicClassId { get; set; }
 public AcademicClass? AcademicClass { get; set; }
 [Required, StringLength(450)] public string StudentId { get; set; } = "";
 public bool IsActive { get; set; } = true;
}
public enum AutomaticViolationAction { [Display(Name="Lock for teacher review (can resume)")] LockForReview = 0, [Display(Name="Final submit (cannot resume)")] FinalSubmit = 1 }
