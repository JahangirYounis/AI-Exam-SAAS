using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class Subject
{
    [Key]
    public int SubjectId { get; set; }


    // ============================================================
    // SUBJECT
    // ============================================================

    [Required]
    [StringLength(150)]
    [Display(Name = "Subject Name")]
    public string SubjectName { get; set; } = string.Empty;


    [StringLength(500)]
    public string? Description { get; set; }


    // ============================================================
    // STATUS
    // ============================================================

    public bool IsActive { get; set; } = true;

    public bool IsLocked { get; set; }


    // ============================================================
    // AUDIT
    // ============================================================

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public DateTime? UpdatedDate { get; set; }


    // ============================================================
    // QUESTION BANK
    // ============================================================

    public virtual ICollection<Question> Questions { get; set; }
        = new List<Question>();


    // ============================================================
    // EXAM PARTS
    // ============================================================

    public virtual ICollection<ExamSubjectPart> ExamParts { get; set; }
        = new List<ExamSubjectPart>();


    // ============================================================
    // QUESTION BANK HELPERS
    // ============================================================

    public const int MinimumQuestionBankSize = 50;

    [NotMapped]
    public int ActiveQuestionCount =>
        Questions?.Count(q => q.IsActive) ?? 0;


    [NotMapped]
    public bool HasMinimumQuestionBank =>
        ActiveQuestionCount >= MinimumQuestionBankSize;


    [NotMapped]
    public bool HasEnoughQuestions =>
        HasMinimumQuestionBank;
}
