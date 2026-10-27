using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models
{
    public class ExamSessionAttempt
    {
        [MaxLength(2000)] public string? DeviceHealthJson { get; set; }
        [Key]
        public int SessionAttemptId { get; set; }

        //==========================================================
        // EXAM ATTEMPT
        //==========================================================

        [Required]
        public int AttemptId { get; set; }

        [ForeignKey(nameof(AttemptId))]
        public virtual ExamAttempt? Attempt { get; set; }

        //==========================================================
        // SESSION
        //==========================================================

        [Required]
        public int SessionId { get; set; }

        [ForeignKey(nameof(SessionId))]
        public virtual ExamSession? Session { get; set; }

        //==========================================================
        // TIME
        //==========================================================

        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        //==========================================================
        // STATUS
        //==========================================================

        public AttemptStatus Status { get; set; }
            = AttemptStatus.NotStarted;

        //==========================================================
        // RESULT
        //==========================================================

        [Column(TypeName = "decimal(8,2)")]
        public decimal MarksObtained { get; set; }

        [Column(TypeName = "decimal(8,2)")]
        public decimal TotalMarks { get; set; }

        public int CorrectAnswers { get; set; }

        public int WrongAnswers { get; set; }

        public int SkippedAnswers { get; set; }

        // Last question visible to the candidate. This lets a reviewed,
        // unblocked attempt resume at the exact saved question.
        public int? LastQuestionId { get; set; }
        public int CurrentQuestionIndex { get; set; }
        public int? RemainingTimeSeconds { get; set; }
        public string? SelectedAnswersJSON { get; set; }
        public DateTime? FrozenAtUtc { get; set; }
        // Small bounded server-owned incident state. Evidence remains in ProctoringLogs/storage.
        public string? ProctoringIncidentsJson { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; } = [];

        //==========================================================
        // NAVIGATION
        //==========================================================

        public virtual ICollection<StudentAnswer> Answers { get; set; }
            = new List<StudentAnswer>();
    }
}
