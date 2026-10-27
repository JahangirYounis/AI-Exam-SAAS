using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public enum AttemptStatus
{
    NotStarted,
    InProgress,
    ProctoringBlocked,
    Completed,
    AutoSubmitted,
    Disqualified,
    Cancelled
}

public class ExamAttempt
{
    [Key] public int AttemptId { get; set; }

    [Required] public string StudentId { get; set; } = string.Empty;
    [ForeignKey(nameof(StudentId))] public virtual ApplicationUser? Student { get; set; }

    [Required] public int ExamId { get; set; }
    [ForeignKey(nameof(ExamId))] public virtual Exam? Exam { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.NotStarted;

    [Column(TypeName="decimal(8,2)")] public decimal TotalMarksObtained { get; set; }
    [Column(TypeName="decimal(8,2)")] public decimal TotalMarks { get; set; }
    [Column(TypeName="decimal(6,2)")] public decimal Percentage { get; set; }
    public bool IsPassed { get; set; }

    public DateTime? ResultReleasedAtUtc { get; set; }
    [StringLength(450)] public string? ResultReviewedBy { get; set; }
    public bool SubmittedForViolations { get; set; }
    public int TotalViolations { get; set; }
    public int StrikeCount { get; set; }
    public long StateVersion { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public DateTime? LastViolationAtUtc { get; set; }
    public DateTime? TeacherResumedAtUtc { get; set; }
    public int HighSeverityViolations { get; set; }
    public bool IsDisqualified { get; set; }

    [NotMapped]
    public int SuspicionScore => 0;

    [NotMapped]
    public string ProctoringRiskLevel => "Not active";

    public virtual ICollection<ExamSessionAttempt> SessionAttempts { get; set; } = new List<ExamSessionAttempt>();
    public virtual ExamResult? Result { get; set; }

    [NotMapped] public DateTime StartTime { get => StartedAt; set => StartedAt = value; }
    [NotMapped] public DateTime? EndTime { get => CompletedAt; set => CompletedAt = value; }
}
