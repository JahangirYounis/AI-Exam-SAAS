using Microsoft.Extensions.Options;

namespace AiExamProctoring.Services.Proctoring;

/// <summary>
/// Intentional safe no-op until a DB-reference-aware, approved retention workflow
/// exists. Previously this worker deleted physical media by filesystem timestamp
/// even while appeals, grants and incident references remained active. The
/// existing encrypted originals and log/audit records are never removed here.
/// </summary>
public sealed class ProctoringEvidenceCleanupService : BackgroundService
{
    private readonly ProctoringOptions _options;
    private readonly ILogger<ProctoringEvidenceCleanupService> _logger;

    public ProctoringEvidenceCleanupService(
        IWebHostEnvironment environment,
        IOptions<ProctoringOptions> options,
        ILogger<ProctoringEvidenceCleanupService> logger, IServiceScopeFactory scopes)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.EnableAutomaticEvidenceDeletion)
            _logger.LogWarning("Evidence deletion was requested, but automatic deletion is intentionally disabled until a referenced-file and appeal-aware archival procedure is implemented and approved.");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
