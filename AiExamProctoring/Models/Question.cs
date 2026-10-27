using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public class Question
{
    [Key]
    public int QuestionId { get; set; }


    // ============================================================
    // SUBJECT / QUESTION BANK
    // ============================================================

    [Required]
    [Display(Name = "Subject")]
    public int SubjectId { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? Subject { get; set; }


    // ============================================================
    // QUESTION TEXT
    // ============================================================

    [Required]
    [StringLength(3000, MinimumLength = 2)]
    [Display(Name = "Question")]
    public string QuestionText { get; set; } = string.Empty;


    [StringLength(1000)]
    public string? ImagePath { get; set; }


    [StringLength(3000)]
    public string? Explanation { get; set; }


    // ============================================================
    // OPTIONS
    // ============================================================

    [Required]
    [StringLength(1000)]
    public string OptionA { get; set; } = string.Empty;


    [Required]
    [StringLength(1000)]
    public string OptionB { get; set; } = string.Empty;


    [Required]
    [StringLength(1000)]
    public string OptionC { get; set; } = string.Empty;


    [Required]
    [StringLength(1000)]
    public string OptionD { get; set; } = string.Empty;


    [StringLength(1000)]
    public string? OptionE { get; set; }


    // ============================================================
    // CORRECT ANSWER
    // ============================================================

    [Required]
    [StringLength(1)]
    [RegularExpression(
        "^[ABCDE]$",
        ErrorMessage =
            "Correct answer must be A, B, C, D or E.")]
    public string CorrectAnswer { get; set; } = "A";


    // ============================================================
    // QUESTION BANK ORGANIZATION
    // ============================================================

    [StringLength(150)]
    public string? Topic { get; set; }


    [StringLength(500)]
    public string? Tags { get; set; }


    public int DisplayOrder { get; set; }


    // ============================================================
    // STATUS
    // ============================================================

    public bool IsActive { get; set; } = true;

    public bool IsVerified { get; set; }

    public bool IsLocked { get; set; }


    // ============================================================
    // AUDIT
    // ============================================================

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public DateTime? UpdatedDate { get; set; }


    [StringLength(450)]
    public string? CreatedBy { get; set; }


    [StringLength(450)]
    public string? UpdatedBy { get; set; }


    // ============================================================
    // RELATIONSHIPS
    // ============================================================

    public virtual ICollection<ExamQuestion> ExamQuestions { get; set; }
        = new List<ExamQuestion>();


    public virtual ICollection<StudentAnswer> StudentAnswers { get; set; }
        = new List<StudentAnswer>();


    // ============================================================
    // RUNTIME ONLY
    // ============================================================
    //
    // These values are NOT stored in Questions table.
    //
    // Marks are assigned by ExamQuestion.
    //

    [NotMapped]
    public decimal ExamMarks { get; set; } = 1m;


    [NotMapped]
    public decimal ExamNegativeMarks { get; set; } = 0m;


    // ============================================================
    // DISPLAY HELPERS
    // ============================================================

    [NotMapped]
    public string CorrectAnswerText =>
        CorrectAnswer switch
        {
            "A" => OptionA,
            "B" => OptionB,
            "C" => OptionC,
            "D" => OptionD,
            "E" => OptionE ?? string.Empty,
            _ => string.Empty
        };


    [NotMapped]
    public int QuestionOptionCount =>
        string.IsNullOrWhiteSpace(OptionE)
            ? 4
            : 5;
}