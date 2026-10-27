using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class ExamSession
{
    [Key]
    public int SessionId { get; set; }


    // ============================================================
    // EXAM
    // ============================================================

    [Required]
    public int ExamId { get; set; }

    [ForeignKey(nameof(ExamId))]
    public virtual Exam? Exam { get; set; }


    // ============================================================
    // SESSION INFORMATION
    // ============================================================

    [Range(1, 20)]
    public int SessionNumber { get; set; }


    [Required]
    [StringLength(150)]
    public string SessionTitle { get; set; } = string.Empty;


    [StringLength(1000)]
    public string? Instructions { get; set; }


    // ============================================================
    // AUTOMATIC SESSION TIME
    // ============================================================

    [Range(1, 600)]
    public int DurationMinutes { get; set; }


    [Range(0, 10)]
    [Display(Name = "Rest After Session (Minutes)")]
    public int RestAfterMinutes { get; set; }


    public int DisplayOrder { get; set; }


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
    // QUESTIONS / SUBJECTS
    // ============================================================

    public virtual ICollection<ExamQuestion> ExamQuestions { get; set; }
        = new List<ExamQuestion>();


    public virtual ICollection<ExamSessionSubject> SessionSubjects { get; set; }
        = new List<ExamSessionSubject>();


    public virtual ICollection<ExamSessionAttempt> Attempts { get; set; }
        = new List<ExamSessionAttempt>();


    // ============================================================
    // CALCULATED VALUES
    // ============================================================

    [NotMapped]
    public int TotalQuestions =>
        ExamQuestions?.Count(x => x.IsActive) ?? 0;


    [NotMapped]
    public decimal TotalMarks =>
        ExamQuestions?
            .Where(x => x.IsActive)
            .Sum(x => x.Marks) ?? 0m;
}