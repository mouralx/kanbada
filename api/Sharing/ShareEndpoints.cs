using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class ShareEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api = api.MapGroup("").WithTags("Sharing");
        api.MapGet("/workspaces/{id}/cards/{cardId}/share", async (string id, string cardId, HttpContext ctx, WorkspaceResolver db, KanbadaDbContext context, WorkspaceStore store) =>
        {
            var user = Auth.User(ctx);
            var workspace = await db.WorkspaceId(id, user);
            await store.Read(workspace, user);
            var share = await context.Shares.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.CardId == cardId.ToUpperInvariant());
            return share is null ? Results.Json((object?)null) : Results.Ok(new { share.Token, share.Access, share.ExpiresAt, share.CreatedAt, cardId, workspaceId = id, sourcePrefix = "" });
        });
        api.MapPost("/workspaces/{id}/cards/{cardId}/share", async (string id, string cardId, ShareInput input, HttpContext ctx, WorkspaceResolver db, KanbadaDbContext context, WorkspaceStore store) =>
        {
            if (!new[]
            {
                "signed-in",
                "members"
            }.Contains(input.Access) || !new[]
            {
                0,
                7,
                30
            }.Contains(input.Days))
                throw new ApiError(400, "Invalid sharing settings.");
            var user = Auth.User(ctx);
            var workspace = await db.WorkspaceId(id, user);
            var state = (await store.Read(workspace, user)).State;
            cardId = cardId.ToUpperInvariant();
            if (!WorkspaceJson.Items(state, "tasks").Any(t => WorkspaceJson.Text(t, "id") == cardId))
                throw new ApiError(404, "Card not found.");
            var token = Auth.Token();
            var at = DateTimeOffset.UtcNow;
            DateTimeOffset? expires = input.Days == 0 ? null : at.AddDays(input.Days);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Shares.Where(x => x.WorkspaceId == workspace && x.CardId == cardId).ExecuteDeleteAsync();
            context.Shares.Add(new ShareEntity { Token = token, WorkspaceId = workspace, CardId = cardId, CreatorId = user, Access = input.Access, ExpiresAt = expires, CreatedAt = at });
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return new
            {
                token,
                workspaceId = id,
                cardId,
                access = input.Access,
                expiresAt = expires,
                createdAt = at,
                sourcePrefix = ""
            };
        });
        api.MapDelete("/shares/{token}", async (string token, HttpContext ctx, KanbadaDbContext context) =>
        {
            var user = Auth.User(ctx);
            await context.Shares.Where(x => x.Token == token && context.Members.Any(m => m.WorkspaceId == x.WorkspaceId && m.UserId == user)).ExecuteDeleteAsync();
            return Results.NoContent();
        });
        api.MapGet("/shares/{token}", async (string token, HttpContext ctx, WorkspaceResolver db, KanbadaDbContext context, WorkspaceStore store) => await Resolve(token, Auth.User(ctx), context, store));
        api.MapGet("/shares/{token}/files/{id:guid}", async (string token, Guid id, HttpContext ctx, WorkspaceResolver db, KanbadaDbContext context, WorkspaceStore store) =>
        {
            var result = await Resolve(token, Auth.User(ctx), context, store);
            if (!WorkspaceJson.Items(result["task"], "attachments").Any(f => WorkspaceJson.Text(f, "id") == id.ToString()))
                throw new ApiError(404, "File not found.");
            var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new ApiError(404, "File not found.");
            return Results.File(file.Bytes, "application/octet-stream", file.Name);
        });
    }

    public static async Task<JsonObject> Resolve(string token, Guid user, KanbadaDbContext context, WorkspaceStore store)
    {
        var link = await context.Shares.AsNoTracking().SingleOrDefaultAsync(x => x.Token == token && (x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow))
            ?? throw new ApiError(404, "This link has expired or is no longer available.");
        var workspace = link.WorkspaceId;
        var card = link.CardId;
        var access = link.Access;
        if (access == "members" && !await context.Members.AnyAsync(x => x.WorkspaceId == workspace && x.UserId == user))
            throw new ApiError(403, "This account does not have access to this card.");
        var share = new JsonObject
        {
            ["token"] = token,
            ["cardId"] = card,
            ["workspaceId"] = workspace.ToString(),
            ["access"] = access,
            ["sourcePrefix"] = "share:" + token,
            ["expiresAt"] = link.ExpiresAt?.ToString("O"),
            ["createdAt"] = link.CreatedAt.ToString("O")
        };
        var state = (await store.Read(workspace, user, true)).State;
        var task = WorkspaceJson.Items(state, "tasks").FirstOrDefault(t => WorkspaceJson.Text(t, "id") == card) ?? throw new ApiError(404, "Card not found.");
        var data = new JsonObject
        {
            ["workspace"] = state["workspace"]!.DeepClone(),
            ["labels"] = state["labels"]!.DeepClone(),
            ["projects"] = new JsonArray(WorkspaceJson.Items(state, "projects").Where(p => WorkspaceJson.Text(p, "id") == WorkspaceJson.Text(task, "project")).Select(p => p!.DeepClone()).ToArray())
        };
        return new JsonObject
        {
            ["share"] = share,
            ["task"] = task.DeepClone(),
            ["data"] = data
        };
    }
}
