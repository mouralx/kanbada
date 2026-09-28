using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public sealed class JiraApprovedHostEntity
{
    public string Authority { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
}

public static class JiraHostApproval
{
    public sealed record HostInput(string BaseUrl);

    public static void Map(RouteGroupBuilder api)
    {
        var routes = api.MapGroup("/jira").WithTags("Jira synchronization");
        routes.MapGet("/hosts", async (KanbadaDbContext db, PlatformAdmins admins, IConfiguration configuration, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                canManage = admins.Contains(Auth.User(ctx)),
                hosts = await db.Set<JiraApprovedHostEntity>().AsNoTracking().OrderBy(h => h.Authority).ToListAsync(ct),
                configuredHosts = (configuration["Jira:AllowedHosts"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            });
        });
        routes.MapPost("/hosts", async (HostInput input, KanbadaDbContext db, PlatformAdmins admins, HttpContext ctx, CancellationToken ct) =>
        {
            admins.Require(Auth.User(ctx));
            var authority = JiraDestinationPolicy.Parse(input.BaseUrl).Authority.ToLowerInvariant();
            if (!await db.Set<JiraApprovedHostEntity>().AnyAsync(h => h.Authority == authority, ct))
            {
                db.Add(new JiraApprovedHostEntity { Authority = authority, ApprovedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { authority });
        });
        routes.MapDelete("/hosts", async ([Microsoft.AspNetCore.Mvc.FromBody] HostInput input, KanbadaDbContext db, PlatformAdmins admins, HttpContext ctx, CancellationToken ct) =>
        {
            admins.Require(Auth.User(ctx));
            var authority = JiraDestinationPolicy.Parse(input.BaseUrl).Authority.ToLowerInvariant();
            await db.Set<JiraApprovedHostEntity>().Where(h => h.Authority == authority).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}
