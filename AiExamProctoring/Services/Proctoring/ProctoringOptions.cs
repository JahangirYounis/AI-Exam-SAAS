namespace AiExamProctoring.Services.Proctoring;

/// <summary>
/// Evidence settings and metadata for the integrated server-side strike policy.
/// </summary>
public sealed class ProctoringOptions
{
    public const string SectionName = "Proctoring";

    public bool Enabled { get; set; } = false;

    // Neutral evidence infrastructure is preserved for future versioned module
    // results. The current browser records camera-only clips; it never requests
    // Entire Screen sharing.
    public bool CaptureEvidence { get; set; } = true;
    public int MaxEvidenceBytes { get; set; } = 200_000;
    public int MaxVideoEvidenceBytes { get; set; } = 18_000_000;
    public int EvidenceClipSeconds { get; set; } = 10;
    public int EvidenceRetentionDays { get; set; } = 3_650;
    // Explicitly opt in after retention-policy approval and a verified backup.
    // File deletion must never rely on file age alone while DB references exist.
    public bool EnableAutomaticEvidenceDeletion { get; set; } = false;
    public double IncidentRecoverySeconds { get; set; } = 1;
    public double IncidentCooldownSeconds { get; set; } = 10;
    public double IncidentMaximumSampleGapSeconds { get; set; } = 15;
    public bool CorrelateAttentionSpeech { get; set; }
    public double AttentionPersistenceSeconds { get; set; } = 2;
    public double MultiplePersonPersistenceSeconds { get; set; } = 1;
    public double FaceAbsencePersistenceSeconds { get; set; } = 3;
    public double ObjectPersistenceSeconds { get; set; } = 1;
    // Configure these via Proctoring:Device* in appsettings or environment.
    public double DeviceHoldConfirmationSeconds { get; set; } = 9;
    public double DeviceHealthTelemetryTimeoutSeconds { get; set; } = 15;
    public double DeviceRecoveryConfirmationSeconds { get; set; } = 2;
    public int DeviceRecoverySamples { get; set; } = 3;

    public string ModelName { get; set; } = "Server-authoritative proctoring policy";
    public string ModelVersion { get; set; } = "Model6-v2-persisted-lock; Model5-integrated";
}
