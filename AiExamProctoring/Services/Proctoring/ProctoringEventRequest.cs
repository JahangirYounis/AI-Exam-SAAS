using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.Services.Proctoring;

/// <summary>
/// Validated transport contract for browser-owned session observations. These
/// events are stored for human review and never become automatic violations.
/// </summary>
public sealed class ProctoringEventRequest
{
    public int AttemptId { get; set; }
    public int SessionAttemptId { get; set; }

    [Required, StringLength(40)]
    public string AlertType { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public decimal? ConfidenceScore { get; set; }
    public int? DetectedFaceCount { get; set; }
    public int? DurationSeconds { get; set; }

    [StringLength(400)]
    public string? Transcript { get; set; }

    public decimal? AudioLevel { get; set; }

    [StringLength(64)]
    public string? ClientEventId { get; set; }

    public DateTime? FrameCapturedAt { get; set; }

    // Kept outside the entity and accepted only after strict size/signature checks.
    public string? EvidenceDataUrl { get; set; }
}

/// <summary>
/// A short browser-recorded evidence clip attached only after the server has
/// accepted a proctoring event. Ownership and recency are revalidated server-side.
/// </summary>
public sealed class ProctoringEvidenceUploadRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(300000)] public string? ScreenshotDataUrl { get; set; }
    public int LogId { get; set; }

    public bool IncludesAudio { get; set; }

    [Required]
    public IFormFile? EvidenceFile { get; set; }
}
