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
    public int MaxVideoEvidenceBytes { get; set; } = 8_000_000;
    public int EvidenceClipSeconds { get; set; } = 10;
    public int EvidenceRetentionDays { get; set; } = 3_650;
    public double IncidentRecoverySeconds { get; set; } = 1;
    public double IncidentCooldownSeconds { get; set; } = 10;
    public double IncidentMaximumSampleGapSeconds { get; set; } = 15;
    public double AttentionPersistenceSeconds { get; set; } = .5;
    public double MultiplePersonPersistenceSeconds { get; set; } = .5;
    public double FaceAbsencePersistenceSeconds { get; set; } = 10;
    public double ObjectPersistenceSeconds { get; set; } = 1;

    public string ModelName { get; set; } = "Server-authoritative proctoring policy";
    public string ModelVersion { get; set; } = "Model6-v2-persisted-lock; Model5-integrated";
}
