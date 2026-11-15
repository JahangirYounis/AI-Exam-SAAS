using System.ComponentModel.DataAnnotations;
namespace AiExamProctoring.Models;
public sealed class EvidenceGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LogId { get; set; }
    public ProctoringLog? Log { get; set; }
    [Required, StringLength(450)] public string RecipientId { get; set; } = "";
    [Required, StringLength(450)] public string OwnerId { get; set; } = "";
    [Required, StringLength(20)] public string Kind { get; set; } = "";
    [Required, StringLength(500)] public string StorageKey { get; set; } = "";
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
