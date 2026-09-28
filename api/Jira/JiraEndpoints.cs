using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class JiraEndpoints
{
    public sealed record ResolveInput(string JiraIssueId);
    public sealed record CardLink(string Key, string Url);

    public static async Task<List<CardLink>> CardLinks(string id, string cardId, Guid user,
        WorkspaceResolver resolver, KanbadaDbContext db, JiraClient jira, CancellationToken ct)
    {
        var workspace = await resolver.WorkspaceId(id, user);
        if (!await db.Members.AnyAsync(m => m.WorkspaceId == workspace && m.UserId == user, ct)
            || !await db.Cards.AnyAsync(c => c.WorkspaceId == workspace && c.Id == cardId, ct))
            throw new ApiError(404, "Card not found or access denied.");
        var links = await (from link in db.Set<JiraLinkEntity>().AsNoTracking()
                           join connection in db.Set<JiraConnectionEntity>() on link.ConnectionId equals connection.Id
                           where connection.WorkspaceId == workspace && link.CardId == cardId
                               && link.JiraKey != null && link.JiraIssueId != null && !link.CreationPending
                           orderby link.JiraKey
                           select new { link.JiraKey, Connection = connection }).ToListAsync(ct);
        var result = new List<CardLink>();
        foreach (var link in links)
            result.Add(new CardLink(link.JiraKey!, await jira.BrowseUrl(link.Connection, link.JiraKey!, ct)));
        return result;
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/workspaces/{id}/cards/{cardId}/jira", async (string id, string cardId,
            WorkspaceResolver resolver, KanbadaDbContext db, JiraClient jira, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(new { links = await CardLinks(id, cardId, Auth.User(ctx), resolver, db, jira, ct) }); }
            catch (JiraSyncException error) { throw new ApiError(502, error.Message); }
            catch (HttpRequestException) { throw new ApiError(502, "Cannot load the Jira card link."); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiError(504, "Jira request timed out."); }
        }).WithTags("Jira synchronization");
        var routes = api.MapGroup("/workspaces/{id}/projects/{projectId}").WithTags("Jira synchronization");
        routes.MapPost("/jira/test", async (string id, string projectId, JiraConnectionInput input, JiraSettings settings, JiraClient jira, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            var connection = await settings.ForTest(workspace, projectId, input, ct);
            try { return Results.Ok(await jira.Metadata(connection, ct)); }
            catch (JiraSyncException error) { throw new ApiError(400, error.Message); }
            catch (HttpRequestException) { throw new ApiError(502, "Cannot connect to Jira. Check its hostname, TLS certificate and network connectivity."); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiError(504, "Jira request timed out."); }
        }).RequireRateLimiting("auth");
        routes.MapGet("/jira", async (string id, string projectId, JiraSettings settings, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { connection = await settings.Read(await settings.Authorize(id, projectId, Auth.User(ctx), ct), projectId, ct) });
        });
        routes.MapPut("/jira", async (string id, string projectId, JiraConnectionInput input, JiraSettings settings, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            await settings.Save(workspace, projectId, input, ct);
            return Results.Ok(await settings.Read(workspace, projectId, ct));
        });
        routes.MapPost("/jira/run", async (string id, string projectId, JiraSettings settings, KanbadaDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            var c = await settings.Find(workspace, projectId, ct) ?? throw new ApiError(404, "Configure Jira first.");
            if (!c.Enabled) throw new ApiError(400, "Enable synchronization before requesting a run.");
            c.RequestedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { queued = true });
        });
        routes.MapPost("/jira/links/{linkId:guid}/resolve", async (string id, string projectId, Guid linkId, ResolveInput input, JiraSettings settings, KanbadaDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var workspace = await settings.Authorize(id, projectId, Auth.User(ctx), ct);
            var c = await settings.Find(workspace, projectId, ct) ?? throw new ApiError(404, "Configure Jira first.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(input.JiraIssueId ?? "", "^[0-9]{1,20}$"))
                throw new ApiError(400, "Provide the numeric ID of the Jira issue created by the interrupted request.");
            var link = await db.Set<JiraLinkEntity>().SingleOrDefaultAsync(l => l.ConnectionId == c.Id && l.Id == linkId && l.CreationPending, ct)
                ?? throw new ApiError(404, "Pending link not found.");
            link.JiraIssueId = input.JiraIssueId;
            link.CreationPending = false;
            link.LastError = null;
            await JiraCardPolicy.InvalidateWorkspace(db, workspace, ct);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
