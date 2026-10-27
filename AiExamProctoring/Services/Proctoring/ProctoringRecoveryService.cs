using AiExamProctoring.Data;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services.Proctoring;

// Recover the narrow crash window between evidence persistence and policy application.
public sealed class ProctoringRecoveryService(IServiceScopeFactory scopes, ILogger<ProctoringRecoveryService> logger) : BackgroundService
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
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Pending proctoring decisions will be retried"); }
        }
    }
}
