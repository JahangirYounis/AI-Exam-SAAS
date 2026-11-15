using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models
{
    public class ExamResult
    {
        [Key]
        public int ResultId { get; set; }

        //==========================================================
        // ATTEMPT
        //==========================================================

        [Required]
        public int AttemptId { get; set; }

        [ForeignKey(nameof(AttemptId))]
        public virtual ExamAttempt? Attempt { get; set; }

        //==========================================================
        // RESULT
        //==========================================================

        [Column(TypeName = "decimal(8,2)")]
        public decimal TotalMarksObtained { get; set; }

        [Column(TypeName = "decimal(8,2)")]
        public decimal TotalMarks { get; set; }

        [Column(TypeName = "decimal(6,2)")]
        public decimal Percentage { get; set; }

        public bool IsPassed { get; set; }
        public decimal ReviewDeduction { get; set; }

        public int CorrectAnswers { get; set; }

        public int WrongAnswers { get; set; }

        public int SkippedAnswers { get; set; }

        //==========================================================
        // CERTIFICATE
        //==========================================================

        [StringLength(100)]
        public string? CertificateNumber { get; set; }

        [StringLength(64)] public string? CertificateReference { get; set; }
        public DateTime? CertificateIssuedAtUtc { get; set; }
        public DateTime? CertificateExpiresAtUtc { get; set; }
        public DateTime? CertificateRevokedAtUtc { get; set; }
        [StringLength(450)] public string? CertificateRevokedBy { get; set; }
        [StringLength(1000)] public string? CertificateRevocationReason { get; set; }
        [StringLength(12000)] public string? CertificateSnapshotJson { get; set; }

        public DateTime GeneratedDate { get; set; } = DateTime.Now;
    }
}