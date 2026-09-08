using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class WorkspaceEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api = api.MapGroup("").WithTags("Workspaces");
        api.MapGet("/workspaces", async (WorkspaceStore store, HttpContext ctx) => await store.List(Auth.User(ctx)));
        api.MapPost("/workspaces", async (WorkspaceInput input, WorkspaceStore store, HttpContext ctx) =>
        {
            var user = Auth.User(ctx);
            var id = await store.Create(user, input.Name);
            return Results.Ok((await store.Read(id, user)).State);
        });
        api.MapGet("/workspaces/{id}", async (string id, WorkspaceResolver db, KanbadaDbContext context, WorkspaceStore store, HttpContext ctx) =>
        {
            var user = Auth.User(ctx);
            var workspace = await db.WorkspaceId(id, user);
            var version = await context.Workspaces.AsNoTracking().Where(x => x.Id == workspace && context.Members.Any(m => m.WorkspaceId == workspace && m.UserId == user)).Select(x => (long?)x.Version).SingleOrDefaultAsync()
                ?? throw new ApiError(404, "Workspace not found or access denied.");
            ctx.Response.Headers.ETag = "\"" + version + "\"";
            ctx.Response.Headers.CacheControl = "private, no-store";
            if (ctx.Request.Headers.IfNoneMatch.ToString().Split(',').Any(tag => tag.Trim() == "\"" + version + "\""))
                return Results.StatusCode(304);
            var result = await store.Read(workspace, user);
            ctx.Response.Headers.ETag = "\"" + result.Version + "\"";
            return Results.Ok(result.State);
        });
        api.MapPatch("/workspaces/{id}", async (string id, ChangeRequest request, WorkspaceResolver resolver, WorkspaceStore store, HttpContext ctx) =>
        {
            if (!long.TryParse(ctx.Request.Headers.IfMatch.ToString().Trim('"'), out var expected))
                throw new ApiError(428, "An If-Match workspace version is required.");
            var user = Auth.User(ctx);
            var workspace = await resolver.WorkspaceId(id, user);
            var before = await store.Read(workspace, user);
            if (before.Version != expected) throw new ApiError(409, "This workspace changed. Refresh before saving.");
            var next = StateChanges.Apply(before.State, request.Changes);
            var saved = await store.Save(workspace, user, next, expected);
            var version = saved["version"]!.GetValue<long>();
            ctx.Response.Headers.ETag = "\"" + version + "\"";
            return new ChangeResult(version, StateChanges.Diff(before.State, saved));
        }).WithName("ApplyWorkspaceChanges");
        api.MapPut("/workspaces/{id}", async (string id, JsonObject state, WorkspaceResolver db, WorkspaceStore store, HttpContext ctx) =>
        {
            if (!long.TryParse(ctx.Request.Headers.IfMatch.ToString().Trim('"'), out var version))
                throw new ApiError(428, "An If-Match workspace version is required.");
            var user = Auth.User(ctx);
            return await store.Save(await db.WorkspaceId(id, user), user, state, version);
        });
        api.MapDelete("/workspaces/{id}", async (string id, WorkspaceResolver db, WorkspaceStore store, HttpContext ctx) =>
        {
            var user = Auth.User(ctx);
            await store.Delete(await db.WorkspaceId(id, user), user);
            return Results.NoContent();
        });
        api.MapGet("/workspaces/{id}/export", async (string id, WorkspaceResolver db, WorkspaceStore store, HttpContext ctx) =>
        {
            var user = Auth.User(ctx);
            return Results.File(System.Text.Encoding.UTF8.GetBytes((await store.Read(await db.WorkspaceId(id, user), user)).State.ToJsonString()), "application/json", "workspace.json");
        });
        // Granular reads mirror the state collections used by the portal. Writes use an atomic, versioned workspace transaction.
        api.MapGet("/workspaces/{id}/{collection}", async (string id, string collection, WorkspaceResolver db, WorkspaceStore store, HttpContext ctx) =>
        {
            if (!new[]
            {
                "projects",
                "tasks",
                "members",
                "statuses",
                "buckets",
                "labels",
                "swimlanes",
                "notifications",
                "activity"
            }.Contains(collection))
                throw new ApiError(404, "Resource not found.");
            var user = Auth.User(ctx);
            return (await store.Read(await db.WorkspaceId(id, user), user)).State[collection];
        });
        api.MapGet("/workspaces/{id}/cards/{cardId}", async (string id, string cardId, WorkspaceResolver db, WorkspaceStore store, HttpContext ctx) =>
        {
            var user = Auth.User(ctx);
            var state = (await store.Read(await db.WorkspaceId(id, user), user)).State;
            return WorkspaceJson.Items(state, "tasks").FirstOrDefault(t => WorkspaceJson.Text(t, "id").Equals(cardId, StringComparison.OrdinalIgnoreCase)) ?? throw new ApiError(404, "Card not found.");
        });
        api.MapGet("/workspaces/{id}/metrics", async (string id, string? project, string? bucket, string? swimlane, WorkspaceResolver db, WorkspaceMetrics metrics, HttpContext ctx) =>
        {
            var user = Auth.User(ctx);
            return await metrics.Calculate(await db.WorkspaceId(id, user), user, DateOnly.FromDateTime(DateTime.UtcNow), project, bucket, swimlane);
        });
    }
}
