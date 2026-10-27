using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

/// <summary>
/// Represents a question selected for a specific examination.
///
/// IMPORTANT:
/// Question bank does NOT contain marks.
/// Marks are assigned here at examination level.
/// </summary>
public class ExamQuestion
{
    [Key]
    public int ExamQuestionId { get; set; }


    // ============================================================
    // EXAM
    // ============================================================

    [Required]
    public int ExamId { get; set; }

    [ForeignKey(nameof(ExamId))]
    public virtual Exam? Exam { get; set; }


    // ============================================================
    // QUESTION
    // ============================================================

    [Required]
    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public virtual Question? Question { get; set; }


    // ============================================================
    // SESSION
    // ============================================================
    //
    // Session is assigned automatically after the Admin selects
    // the number of sessions.
    //
    // Example:
    //
    // 100 questions / 10 sessions
    //
    // Session 1 ? 10
    // Session 2 ? 10
    // ...
    //
    // ============================================================

    public int? SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    public virtual ExamSession? Session { get; set; }


    // ============================================================
    // EXAM MARKING
    // ============================================================
    //
    // These values belong to the examination,
    // NOT to the question bank.
    //

    [Column(TypeName = "decimal(8,2)")]
    [Range(typeof(decimal), "0.01", "10000")]
    public decimal Marks { get; set; } = 1m;


    [Column(TypeName = "decimal(8,2)")]
    [Range(typeof(decimal), "0", "10000")]
    public decimal NegativeMarks { get; set; } = 0m;


    // ============================================================
    // ORDER / STATUS
    // ============================================================

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;


    // ============================================================
    // AUDIT
    // ============================================================

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}