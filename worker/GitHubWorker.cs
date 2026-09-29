using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

sealed class GitHubWorker(IServiceScopeFactory scopes, ILogger<GitHubWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
                if ((await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any())
                {
                    logger.LogWarning("Waiting for API/database migrations before synchronizing GitHub.");
                    continue;
                }
                var now = DateTimeOffset.UtcNow;
                var due = await db.Set<GitHubConnectionEntity>().AsNoTracking()
                    .Where(c => c.Enabled && (c.NextRunAt <= now || c.RequestedAt != null)
                        && db.Projects.Any(p => p.WorkspaceId == c.WorkspaceId && p.Id == c.ProjectId && !p.Archived))
                    .OrderBy(c => c.NextRunAt).Select(c => c.Id).Take(100).ToListAsync(stoppingToken);
                await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = stoppingToken }, async (id, ct) =>
                {
                    using var runScope = scopes.CreateScope();
                    await runScope.ServiceProvider.GetRequiredService<GitHubSyncEngine>().Run(id, ct);
                });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError("GitHub worker iteration failed ({ErrorType}); retrying in 10 seconds.", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
