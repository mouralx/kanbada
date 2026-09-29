using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var connection = builder.Configuration["ConnectionStrings:Postgres"];
if (string.IsNullOrWhiteSpace(connection))
{
    var password = builder.Configuration["POSTGRES_PASSWORD"] ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres or POSTGRES_PASSWORD.");
    connection = new NpgsqlConnectionStringBuilder
    {
        Host = builder.Configuration["POSTGRES_HOST"] ?? "postgres",
        Database = builder.Configuration["POSTGRES_DB"] ?? "kanbada",
        Username = builder.Configuration["POSTGRES_USER"] ?? "kanbada",
        Password = password
    }.ConnectionString;
}
builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddDbContext<KanbadaDbContext>((services, options) => options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddJira(builder.Configuration, builder.Configuration["Jira:KeyDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, ".data-protection"));
builder.Services.AddGitHub(builder.Configuration["GitHub:KeyDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, ".data-protection"));
builder.Services.AddScoped<WorkspaceResolver>();
builder.Services.AddScoped<WorkspaceMapper>();
builder.Services.AddScoped<WorkspaceStore>();
builder.Services.AddScoped<CardQueries>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ExportProcessor>();
builder.Services.AddHostedService<ExportWorker>();
builder.Services.AddHostedService<JiraWorker>();
builder.Services.AddHostedService<GitHubWorker>();
await builder.Build().RunAsync();

sealed class JiraWorker(IServiceScopeFactory scopes, ILogger<JiraWorker> logger) : BackgroundService
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
                    logger.LogWarning("Waiting for API/database migrations before synchronizing Jira.");
                    continue;
                }
                var now = DateTimeOffset.UtcNow;
                var due = await db.Set<JiraConnectionEntity>().AsNoTracking()
                    .Where(c => c.Enabled && (c.NextRunAt <= now || c.RequestedAt != null)
                        && db.Projects.Any(p => p.WorkspaceId == c.WorkspaceId && p.Id == c.ProjectId && !p.Archived))
                    .OrderBy(c => c.NextRunAt).Select(c => c.Id).Take(100).ToListAsync(stoppingToken);
                foreach (var id in due)
                {
                    using var runScope = scopes.CreateScope();
                    await runScope.ServiceProvider.GetRequiredService<JiraSyncEngine>().Run(id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError("Jira worker iteration failed ({ErrorType}); retrying in 10 seconds.", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
