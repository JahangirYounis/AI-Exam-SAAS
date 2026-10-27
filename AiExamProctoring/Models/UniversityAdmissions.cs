using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public enum UniversityCampus { SBA, SGR, NF }
public enum AdmissionStatus { Submitted, Approved, Rejected }
public enum AdmissionPaymentStatus { AwaitingPayment, Submitted, Verified, Rejected }

public static class UniversityCampuses
{
    public const string UniversityName = "Shaheed Benazir Bhutto University";
    public static string Name(UniversityCampus campus) => campus switch
    {
        UniversityCampus.SBA => "Shaheed Benazir Bhutto University, Shaheed Benazirabad (Main Campus)",
        UniversityCampus.SGR => "Shaheed Benazir Bhutto University, Sanghar Campus",
        UniversityCampus.NF => "Shaheed Benazir Bhutto University, Naushahro Feroze (Sindh)",
        _ => throw new ArgumentOutOfRangeException(nameof(campus))
    };
}

public sealed class UniversityCandidate
{
    public int Id { get; set; }
    [Required, StringLength(450)] public string StudentId { get; set; } = "";
    public ApplicationUser? Student { get; set; }
    public UniversityCampus Campus { get; set; }
    [StringLength(30)] public string? RollNumber { get; set; }
    [StringLength(1024)] public string PasswordHash { get; set; } = "";
    [StringLength(2048)] public string? ProtectedTemporaryPassword { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public int FailedLogins { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class AdmissionApplication
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    [Required, StringLength(450)] public string StudentId { get; set; } = "";
    public ApplicationUser? Student { get; set; }
    public UniversityCampus Campus { get; set; }
    [Required, StringLength(120)] public string Qualification { get; set; } = "";
    [Required, StringLength(200)] public string Institution { get; set; } = "";
    [Range(0, 100000)] public decimal ObtainedMarks { get; set; }
    [Range(1, 100000)] public decimal MaximumMarks { get; set; }
    public decimal FeeDue { get; set; }
    public decimal PaidAmount { get; set; }
    public AdmissionStatus Status { get; set; }
    public AdmissionPaymentStatus PaymentStatus { get; set; }
    [StringLength(40)] public string? PaymentMethod { get; set; }
    [StringLength(100)] public string? PaymentReference { get; set; }
    [StringLength(200)] public string? ReceiptFile { get; set; }
    [StringLength(450)] public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    [StringLength(1000)] public string? VerificationNote { get; set; }
    [StringLength(450)] public string? InvigilatorId { get; set; }
    public ApplicationUser? Invigilator { get; set; }
    public DateTime? ReviewCompletedAtUtc { get; set; }
    [StringLength(450)] public string? ReviewedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class ExamInvigilator
{
    public bool CanEditQuestions { get; set; } = true;
    public bool CanMonitor { get; set; } = true;
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? FirstMonitoringAtUtc { get; set; }
    public DateTime? LastMonitoringAtUtc { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime InvitedAtUtc { get; set; } = DateTime.UtcNow;

    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    [Required, StringLength(450)] public string TeacherId { get; set; } = "";
    public ApplicationUser? Teacher { get; set; }
    [Range(10, 30)] public int Capacity { get; set; } = 20;
    public bool IsActive { get; set; } = true;
}

public sealed class AdmissionQuestionDecision
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public ExamAttempt? Attempt { get; set; }
    public int QuestionId { get; set; }
    public int EvidenceLogId { get; set; }
    public ProctoringLog? EvidenceLog { get; set; }
    public bool CheatingConfirmed { get; set; }
    public decimal DeductedMarks { get; set; }
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
    [Required, StringLength(450)] public string ReviewerId { get; set; } = "";
    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AdmissionAudit
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public int? ApplicationId { get; set; }
    [Required, StringLength(450)] public string ActorId { get; set; } = "";
    [Required, StringLength(80)] public string Action { get; set; } = "";
    [Required, StringLength(2000)] public string Details { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
