using Microsoft.Extensions.Options;

namespace AiExamProctoring.Services.Proctoring;

/// <summary>
/// Removes locally stored image/video evidence after the configured retention window. Database
/// log metadata remains available for the human-review audit trail.
/// </summary>
public sealed class ProctoringEvidenceCleanupService : BackgroundService
{
    private readonly string _evidenceDirectory;
    private readonly ProctoringOptions _options;
    private readonly ILogger<ProctoringEvidenceCleanupService> _logger;

    public ProctoringEvidenceCleanupService(
        IWebHostEnvironment environment,
        IOptions<ProctoringOptions> options,
        ILogger<ProctoringEvidenceCleanupService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _evidenceDirectory = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(environment),
            "ProctoringEvidence");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DeleteExpiredEvidence();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Expired proctoring evidence cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private void DeleteExpiredEvidence()
    {
        if (!Directory.Exists(_evidenceDirectory))
            return;

        var retentionDays = Math.Clamp(_options.EvidenceRetentionDays, 1, 3_650);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var path in Directory.EnumerateFiles(
                     _evidenceDirectory,
                     "attempt-*.*",
                     SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.GetLastWriteTimeUtc(path) < cutoff)
                File.Delete(path);
        }

        // Candidate/exam folders are intentionally human-readable on disk. Remove
        // empty folders after retention cleanup without touching any live evidence.
        foreach (var directory in Directory.EnumerateDirectories(
                     _evidenceDirectory,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(x => x.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }
}
