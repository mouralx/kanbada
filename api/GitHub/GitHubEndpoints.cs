using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class GitHubEndpoints
{
    public sealed record ResolveInput(string? IssueUrl, bool RetryCreation = false);
    public sealed record HostInput(string BaseUrl);
    public sealed record VersionInput(long Version);

    private sealed class Errors : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (GitHubSyncException error) { throw new ApiError(400, error.Message); }
            catch (HttpRequestException) { throw new ApiError(502, "Cannot connect to GitHub. Check its hostname, TLS certificate and network connectivity."); }
            catch (OperationCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested) { throw new ApiError(504, "GitHub request timed out."); }
        }
    }

    public static void Map(RouteGroupBuilder api)
    {
        var routes = api.MapGroup("/workspaces/{id}/projects/{projectId}").WithTags("GitHub synchronization").AddEndpointFilter<Errors>();
        routes.MapGet("/github", async (string id, string projectId, GitHubSettings settings, HttpContext ctx, CancellationToken ct) =>
            Results.Ok(new { connection = await settings.Read(await settings.Authorize(id, projectId, Auth.User(ctx), ct), projectId, ct) }));
        routes.MapPut("/github", async (string id, string projectId, GitHubConnectionInput input, GitHubSettings settings, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            await settings.Save(workspace, projectId, input, ct);
            return Results.Ok(await settings.Read(workspace, projectId, ct));
        });
        routes.MapPost("/github/test", async (string id, string projectId, GitHubConnectionInput input, GitHubSettings settings, GitHubClient github, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            return Results.Ok(await github.Metadata(await settings.ForTest(workspace, projectId, input, ct), ct));
        }).RequireRateLimiting("auth");
        routes.MapPost("/github/users/{login}", async (string id, string projectId, string login, GitHubConnectionInput input, GitHubSettings settings, GitHubClient github, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            return Results.Ok(await github.User(await settings.ForTest(workspace, projectId, input, ct), login, ct));
        }).RequireRateLimiting("auth");
        routes.MapPost("/github/run", async (string id, string projectId, GitHubSettings settings, KanbadaDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            var c = await settings.Find(workspace, projectId, ct) ?? throw new ApiError(404, "Configure GitHub first.");
            if (!c.Enabled) throw new ApiError(400, "Enable synchronization before requesting a run.");
            if (await db.Projects.AnyAsync(p => p.WorkspaceId == workspace && p.Id == projectId && p.Archived, ct))
                throw new ApiError(400, "Restore the archived project before requesting a run.");
            c.RequestedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { queued = true });
        });
        routes.MapPost("/github/pause", async (string id, string projectId, VersionInput input, GitHubSettings settings, KanbadaDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            var c = await settings.Find(workspace, projectId, ct) ?? throw new ApiError(404, "Configure GitHub first.");
            if (c.Version != input.Version) throw new ApiError(409, "GitHub settings changed. Reload before pausing.");
            c.Enabled = false;
            c.RequestedAt = null;
            c.Version++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await settings.Read(workspace, projectId, ct));
        });
        routes.MapPost("/github/links/{linkId:guid}/resolve", async (string id, string projectId, Guid linkId, ResolveInput input,
            GitHubSettings settings, GitHubClient github, KanbadaDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            var c = await settings.Find(workspace, projectId, ct) ?? throw new ApiError(404, "Configure GitHub first.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({"github:" + c.Id}, 0)) AS \"Value\"").SingleAsync(ct);
            if (!locked) throw new ApiError(409, "A GitHub synchronization is still running. Wait before resolving the creation.");
            var link = await db.Set<GitHubLinkEntity>().SingleOrDefaultAsync(l => l.ConnectionId == c.Id && l.Id == linkId && l.CreationPending && l.ContentId == null, ct)
                ?? throw new ApiError(404, "Pending creation not found.");
            if (input.RetryCreation)
            {
                if (!string.IsNullOrWhiteSpace(input.IssueUrl)) throw new ApiError(400, "Choose either an existing issue or an explicitly verified retry.");
                db.Remove(link);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.IssueUrl)) throw new ApiError(400, "Provide the GitHub issue URL created by the interrupted request.");
                var contentId = await github.ResolveIssue(c, input.IssueUrl, ct);
                if (await db.Set<GitHubLinkEntity>().AnyAsync(l => l.ConnectionId == c.Id && l.ContentId == contentId, ct))
                    throw new ApiError(409, "That GitHub issue is already linked to a card.");
                link.ContentId = contentId;
                link.ContentType = "Issue";
                link.CreationPending = false;
                link.LastError = "Issue recovered. The next run will attach it to the project and finish synchronization.";
                await ExternalCardPolicy.InvalidateWorkspace(db, workspace, ct);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });
        api.MapGet("/workspaces/{id}/cards/{cardId}/github", async (string id, string cardId, WorkspaceResolver resolver,
            KanbadaDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var user = Auth.User(ctx);
            var workspace = await resolver.WorkspaceId(id, user);
            if (!await db.Members.AnyAsync(m => m.WorkspaceId == workspace && m.UserId == user, ct)
                || !await db.Cards.AnyAsync(c => c.WorkspaceId == workspace && c.Id == cardId, ct))
                throw new ApiError(404, "Card not found or access denied.");
            var links = await (from link in db.Set<GitHubLinkEntity>().AsNoTracking()
                               join c in db.Set<GitHubConnectionEntity>() on link.ConnectionId equals c.Id
                               where c.WorkspaceId == workspace && link.CardId == cardId && link.ContentId != null && link.Url != null && !link.CreationPending
                               select new { link.DisplayKey, link.Url, c.BaseUrl }).ToListAsync(ct);
            return Results.Ok(new { links = links.Select(l => new { key = l.DisplayKey, url = GitHubDestinationPolicy.BrowserUrl(l.BaseUrl, l.Url!) }) });
        }).WithTags("GitHub synchronization").AddEndpointFilter<Errors>();

        var hosts = api.MapGroup("/github").WithTags("GitHub synchronization").AddEndpointFilter<Errors>();
        hosts.MapGet("/hosts", async (KanbadaDbContext db, PlatformAdmins admins, IConfiguration configuration, HttpContext ctx, CancellationToken ct) =>
            Results.Ok(new
            {
                canManage = admins.Contains(Auth.User(ctx)),
                hosts = await db.Set<GitHubApprovedHostEntity>().AsNoTracking().OrderBy(h => h.Authority).ToListAsync(ct),
                configuredHosts = (configuration["GitHub:AllowedHosts"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            }));
        hosts.MapPost("/hosts", async (HostInput input, KanbadaDbContext db, PlatformAdmins admins, HttpContext ctx, CancellationToken ct) =>
        {
            admins.Require(Auth.User(ctx));
            var authority = GitHubDestinationPolicy.Parse(input.BaseUrl).Authority.ToLowerInvariant();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO github_approved_hosts (authority, approved_at) VALUES ({authority}, {DateTimeOffset.UtcNow}) ON CONFLICT (authority) DO NOTHING", ct);
            return Results.Ok(new { authority });
        });
        hosts.MapDelete("/hosts", async ([Microsoft.AspNetCore.Mvc.FromBody] HostInput input, KanbadaDbContext db, PlatformAdmins admins, HttpContext ctx, CancellationToken ct) =>
        {
            admins.Require(Auth.User(ctx));
            var authority = GitHubDestinationPolicy.Parse(input.BaseUrl).Authority.ToLowerInvariant();
            await db.Set<GitHubApprovedHostEntity>().Where(h => h.Authority == authority).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}
