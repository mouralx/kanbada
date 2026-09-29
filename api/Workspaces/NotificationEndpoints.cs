using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class NotificationEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/workspaces/{id}/notification-feed", async (string id, string? after, WorkspaceResolver resolver, CardQueries access, KanbadaDbContext db, HttpContext ctx) =>
        {
            var user = Auth.User(ctx);
            var workspace = await resolver.WorkspaceId(id, user);
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var version = await access.Authorize(workspace, user);
            var scope = workspace + ":" + user + ":notifications";
            var rows = db.Notifications.AsNoTracking().Where(n => n.WorkspaceId == workspace && (n.RecipientId == null || n.RecipientId == user));
            var cursor = after is null ? null : PageCursor.Read(after, scope, version);
            var total = cursor?.Total ?? await rows.CountAsync();
            if (cursor is not null)
            {
                rows = rows.Where(n => n.Position > cursor.Position || (n.Position == cursor.Position && n.Id.CompareTo(cursor.Id) > 0));
            }
            var found = await rows.OrderBy(n => n.Position).ThenBy(n => n.Id).Take(41).ToListAsync();
            var items = found.Take(40).Select(n => new { n.Id, n.Message, n.At, n.CardId }).ToArray();
            var last = found.Take(40).LastOrDefault();
            var nextCursor = found.Count > 40 && last is not null ? new PageCursor(last.Position, last.Id, version, scope, total).Encode() : null;
            await tx.CommitAsync();
            return new { items, total, nextCursor, version };
        }).WithTags("Workspaces");
        api.MapDelete("/workspaces/{id}/notification-feed", async (string id, string? notificationId, WorkspaceResolver resolver, CardQueries access, KanbadaDbContext db, HttpContext ctx) =>
        {
            if (!long.TryParse(ctx.Request.Headers.IfMatch.ToString().Trim('"'), out var expected))
                throw new ApiError(428, "An If-Match workspace version is required.");
            var user = Auth.User(ctx);
            var workspace = await resolver.WorkspaceId(id, user);
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            if (await access.Authorize(workspace, user) != expected) throw new ApiError(409, "This workspace changed. Refresh before saving.");
            await db.Notifications.Where(n => n.WorkspaceId == workspace && (n.RecipientId == null || n.RecipientId == user)
                && (notificationId == null || n.Id == notificationId)).ExecuteDeleteAsync();
            var entity = await db.Workspaces.SingleAsync(w => w.Id == workspace);
            entity.Version++;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        }).WithTags("Workspaces");
    }
}
