using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

/// <summary>
/// Exam-specific configuration for a subject.
///
/// Subject itself remains independent.
/// Question count is NOT fixed here.
/// Selected questions are stored in ExamQuestion.
/// </summary>
public class ExamSubjectPart
{
    [Key]
    public int ExamSubjectPartId { get; set; }


    // ============================================================
    // EXAM
    // ============================================================

    [Required]
    public int ExamId { get; set; }

    [ForeignKey(nameof(ExamId))]
    public virtual Exam? Exam { get; set; }


    // ============================================================
    // SUBJECT
    // ============================================================

    [Required]
    public int SubjectId { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? Subject { get; set; }


    // ============================================================
    // DISPLAY
    // ============================================================

    public int DisplayOrder { get; set; } = 1;

    public bool IsActive { get; set; } = true;


    // ============================================================
    // OPTIONAL DEFAULT MARKING
    // ============================================================
    //
    // These are defaults only.
    //
    // Actual marks are stored in ExamQuestion.
    //

    [Column(TypeName = "decimal(8,2)")]
    [Range(typeof(decimal), "0.01", "10000")]
    public decimal DefaultMarksPerQuestion { get; set; } = 1m;


    [Column(TypeName = "decimal(8,2)")]
    [Range(typeof(decimal), "0", "10000")]
    public decimal DefaultNegativeMarks { get; set; } = 0m;


    // ============================================================
    // AUDIT
    // ============================================================

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}