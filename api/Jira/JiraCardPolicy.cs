using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace Kanbada.Api;

public static class JiraCardPolicy
{
    public const string ReadOnlyMessage = "This card is managed by Jira and is read-only in Kanbada. Make changes in Jira.";

    public static IQueryable<string> ReadOnlyCardIds(KanbadaDbContext db, Guid workspace) =>
        (from link in db.Set<JiraLinkEntity>()
         join connection in db.Set<JiraConnectionEntity>() on link.ConnectionId equals connection.Id
         where connection.WorkspaceId == workspace && connection.Direction == "jira-to-kanbada"
             && link.JiraIssueId != null && !link.CreationPending
         select link.CardId).Distinct();

    public static void ValidateChanges(JsonObject before, JsonObject after)
    {
        var previous = WorkspaceJson.Items(before, "tasks");
        var next = WorkspaceJson.Items(after, "tasks");
        var nextById = next.ToLookup(task => WorkspaceJson.Text(task, "id"));
        var previousIds = previous.Select(task => WorkspaceJson.Text(task, "id")).ToHashSet();
        var retainedBefore = previousIds.Where(id => nextById.Contains(id)).ToHashSet();
        var oldOrder = previous.Where(task => retainedBefore.Contains(WorkspaceJson.Text(task, "id")))
            .Select((task, index) => new { Id = WorkspaceJson.Text(task, "id"), Index = index }).ToDictionary(x => x.Id, x => x.Index);
        var newOrder = next.Where(task => retainedBefore.Contains(WorkspaceJson.Text(task, "id")))
            .Select(task => WorkspaceJson.Text(task, "id")).Distinct()
            .Select((id, index) => new { Id = id, Index = index }).ToDictionary(x => x.Id, x => x.Index);
        foreach (var card in previous.Where(task => task?["readOnly"]?.GetValue<bool>() == true))
        {
            var id = WorkspaceJson.Text(card, "id");
            var matches = nextById[id].ToArray();
            if (matches.Length != 1) throw new ApiError(403, ReadOnlyMessage);
            var original = card!.DeepClone().AsObject();
            var proposed = matches[0]!.DeepClone().AsObject();
            original.Remove("readOnly");
            proposed.Remove("readOnly");
            if (!JsonNode.DeepEquals(original, proposed) || oldOrder[id] != newOrder[id])
                throw new ApiError(403, ReadOnlyMessage);
        }
    }

    public static async Task InvalidateWorkspace(KanbadaDbContext db, Guid workspaceId, CancellationToken ct)
    {
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == workspaceId, ct);
        workspace.Version++;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
