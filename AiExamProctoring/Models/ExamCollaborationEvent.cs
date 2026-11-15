using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.Models;

// Append-only lifecycle events for future invitation cycles.
// ExamInvigilator remains the sole authority for granting access.
public sealed class ExamCollaborationEvent
{
    public long Id { get; set; }
    public int InvitationId { get; set; }
    public int ExamId { get; set; }
    [Required, StringLength(450)] public string TeacherId { get; set; } = string.Empty;
    [Required, StringLength(450)] public string ActorId { get; set; } = string.Empty;
    [Required, StringLength(32)] public string EventType { get; set; } = string.Empty;
    public bool CanEditQuestions { get; set; }
    public bool CanMonitor { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
