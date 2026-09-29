using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

sealed class ExportWorker(IServiceScopeFactory scopes, ExportProcessor processor, ILogger<ExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
                if ((await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any())
                {
                    logger.LogWarning("Waiting for API/database migrations before processing exports.");
                    continue;
                }
                await processor.Expire(stoppingToken);
                var ids = await db.Exports.AsNoTracking().Where(e => e.Status == "queued" || e.Status == "running")
                    .OrderBy(e => e.CreatedAt).Select(e => e.Id).Take(20).ToArrayAsync(stoppingToken);
                foreach (var id in ids) await processor.Run(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Export worker iteration failed; retrying in three seconds."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
