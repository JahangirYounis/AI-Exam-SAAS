using System.Text.Json;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiExamProctoring.Services.Proctoring;

// Recovers crash-pending decisions AND detects loss of heartbeat after the first
// browser health report. No cheating strike is assigned for connectivity loss.
public sealed class ProctoringRecoveryService(IServiceScopeFactory scopes,
    ILogger<ProctoringRecoveryService> logger, IOptions<ProctoringOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var states = scope.ServiceProvider.GetRequiredService<IProctoringStateService>();
                var pending = await db.ProctoringLogs.Where(l => l.PolicyPending && !l.PolicyProcessed)
                    .OrderBy(l => l.LogId).Select(l => l.LogId).Take(50).ToListAsync(stoppingToken);
                foreach (var id in pending) await states.ApplyLogAsync(id, stoppingToken);

                var now = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(double.IsFinite(options.Value.DeviceHealthTelemetryTimeoutSeconds)
                    ? Math.Clamp(options.Value.DeviceHealthTelemetryTimeoutSeconds, 10, 120) : 15);
                var monitored = await db.ExamSessionAttempts.AsNoTracking()
                    .Where(s => s.Status == AttemptStatus.InProgress && s.FrozenAtUtc == null &&
                        s.DeviceHealthJson != null && s.Attempt!.Status == AttemptStatus.InProgress)
                    .Select(s => new { s.AttemptId, s.SessionAttemptId, s.DeviceHealthJson,
                        s.Attempt!.StudentId }).Take(500).ToListAsync(stoppingToken);
                foreach (var s in monitored)
                {
                    DeviceHealthEpisode? episode = null;
                    try { episode = JsonSerializer.Deserialize<DeviceHealthEpisode>(s.DeviceHealthJson!); }
                    catch (JsonException) { /* Old, malformed telemetry: don't auto-accuse. */ }
                    if (episode is null || episode.IsTechnicalHold) continue;
                    // Only start watchdog after at least one actual browser report.
                    // Server-generated warnings do not advance ClientSeen.
                    var browserSeen = episode.ClientSeen ?? episode.Seen;
                    if (now - browserSeen < timeout) continue;
                    await states.HealthAsync(s.AttemptId, s.SessionAttemptId, s.StudentId,
                        DeviceHealthPolicy.NetworkIssue, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Pending proctoring decisions/device-health checks will be retried"); }
        }
    }
}
