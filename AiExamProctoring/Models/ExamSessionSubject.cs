using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

/// <summary>
/// Stores a summary of how many questions from each subject
/// are present in a particular session.
/// </summary>
public class ExamSessionSubject
{
    [Key]
    public int SessionSubjectId { get; set; }


    // ============================================================
    // SESSION
    // ============================================================

    [Required]
    public int SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    public virtual ExamSession? Session { get; set; }


    // ============================================================
    // SUBJECT
    // ============================================================

    [Required]
    public int SubjectId { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? Subject { get; set; }


    // ============================================================
    // DISTRIBUTION
    // ============================================================

    [Range(1, 10000)]
    public int QuestionsToShow { get; set; }


    [Column(TypeName = "decimal(10,2)")]
    public decimal Marks { get; set; }


    public int DisplayOrder { get; set; }


    // ============================================================
    // STATUS / AUDIT
    // ============================================================

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}