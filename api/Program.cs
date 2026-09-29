using Kanbada.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
var providers = builder.AddKanbada();
var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<RequestSecurityMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TwoFactorEnrollmentMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/api/health", async (KanbadaDbContext db) =>
{
    if (!await db.Database.CanConnectAsync()) return Results.StatusCode(503);
    return Results.Ok(new { status = "healthy", database = "PostgreSQL" });
}).WithTags("Health");
AuthenticationEndpoints.Map(app, providers);
PlatformBranding.Map(app);
var api = app.MapGroup("/api").RequireAuthorization();
WorkspaceEndpoints.Map(api);
ExportEndpoints.Map(api);
NotificationEndpoints.Map(api);
CardEndpoints.Map(api);
JiraEndpoints.Map(api);
JiraHostApproval.Map(api);
GitHubEndpoints.Map(api);
FileEndpoints.Map(api);
ShareEndpoints.Map(api);
InvitationEndpoints.Map(api);
app.MapGet("/api/health/ready", async (Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService health, CancellationToken cancellationToken) =>
{
    var report = await health.CheckHealthAsync(cancellationToken);
    return Results.Text(report.Status.ToString(), "text/plain", statusCode: report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy ? 200 : 503);
}).WithTags("Health");
app.MapKanbadaDocumentation();
await using (var scope = app.Services.CreateAsyncScope())
{
    if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", app.Environment.IsDevelopment()))
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().Initialize();
    else if ((await scope.ServiceProvider.GetRequiredService<KanbadaDbContext>().Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException("Database migrations are pending. Apply them with dotnet ef database update before starting the API.");
    await app.Services.GetRequiredService<PlatformAdmins>().Initialize(app.Configuration, scope.ServiceProvider.GetRequiredService<KanbadaDbContext>());
}
app.Run();
public partial class Program;
