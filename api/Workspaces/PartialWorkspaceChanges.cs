using System.Text.Json.Nodes;

namespace Kanbada.Api;

public sealed record CardEdit(string Id, StateChange[]? Changes = null, JsonObject? Value = null);
public sealed record PartialChangeRequest(StateChange[] Changes, CardEdit[] Upserts, string[] Removed, string[]? Retained = null);

public static class PartialWorkspaceChanges
{
    public static async Task<object> Save(Guid workspace, Guid user, long expected, PartialChangeRequest request, WorkspaceStore store)
    {
        if (request.Changes is null || request.Upserts is null || request.Removed is null
            || request.Changes.Length + request.Upserts.Length + request.Removed.Length is < 1 or > 10000
            || request.Changes.Any(c => c is null || c.Path is null || c.Path == "/tasks" || c.Path.StartsWith("/tasks/")))
            throw new ApiError(400, "Send metadata changes and card changes separately.");
        if (request.Upserts.Any(c => c is null)) throw new ApiError(400, "Invalid card change.");
        var ids = request.Upserts.Select(c => c.Id).Concat(request.Removed).ToArray();
        if (ids.Any(string.IsNullOrEmpty) || ids.Distinct().Count() != ids.Length)
            throw new ApiError(400, "Card changes require unique IDs.");
        var retained = request.Retained ?? [];
        if (retained.Length > 10000 || retained.Any(string.IsNullOrEmpty))
            throw new ApiError(400, "Invalid retained card IDs.");
        var scopeIds = ids.Concat(retained).Distinct().ToArray();
        // Definition/member/project edits can affect cards that have never been downloaded.
        var structural = request.Changes.Any(c => new[] { "projects", "statuses", "buckets", "labels", "swimlanes", "members" }.Contains(c.Path.Split('/').ElementAtOrDefault(1)));
        var before = await store.Read(workspace, user, cardIds: structural ? null : scopeIds, sharedNotificationsOnly: true);
        if (before.Version != expected) throw new ApiError(409, "This workspace changed. Refresh before saving.");
        var next = request.Changes.Length == 0 ? before.State.DeepClone().AsObject() : StateChanges.Apply(before.State, request.Changes);
        var cards = WorkspaceJson.Items(next, "tasks");
        var original = WorkspaceJson.Items(before.State, "tasks").ToDictionary(c => WorkspaceJson.Text(c, "id"));
        foreach (var id in request.Removed)
        {
            var card = cards.FirstOrDefault(c => WorkspaceJson.Text(c, "id") == id);
            if (card is null) throw new ApiError(404, "Card not found.");
            cards.Remove(card);
        }
        foreach (var proposed in request.Upserts)
        {
            var id = proposed.Id;
            original.TryGetValue(id, out var old);
            JsonObject card;
            if (proposed.Value is not null && proposed.Changes is null) card = proposed.Value.DeepClone().AsObject();
            else if (proposed.Value is null && proposed.Changes is { Length: > 0 } changes)
            {
                if (old is null) throw new ApiError(404, "Card not found.");
                if (changes.Any(c => c is null || c.Path is null || !c.Path.StartsWith("/tasks/0/")))
                    throw new ApiError(400, "Card field changes must target /tasks/0/ fields.");
                var changed = StateChanges.Apply(new JsonObject { ["tasks"] = new JsonArray(old.DeepClone()) }, changes);
                card = changed["tasks"]![0]!.AsObject().DeepClone().AsObject();
            }
            else throw new ApiError(400, "Supply either card field changes or a card value.");
            if (WorkspaceJson.Text(card, "id") != id) throw new ApiError(400, "Card identity cannot change.");
            card["history"] = old?["history"]?.DeepClone() ?? new JsonArray();
            card["readOnly"] = old?["readOnly"]?.DeepClone() ?? JsonValue.Create(false);
            var index = cards.ToList().FindIndex(c => WorkspaceJson.Text(c, "id") == id);
            if (index < 0) cards.Add(card);
            else cards[index] = card;
        }
        if (structural) ReconcileReferences(before.State, next, request.Upserts.Select(c => c.Id).ToHashSet());
        var saved = await store.Save(workspace, user, next, expected, structural ? null : scopeIds, sharedNotificationsOnly: true);
        var savedCards = WorkspaceJson.Items(saved, "tasks").ToDictionary(c => WorkspaceJson.Text(c, "id"));
        var suppliedValues = request.Upserts.Where(edit => edit.Value is not null).Select(edit => edit.Id).ToHashSet();
        var updated = scopeIds.Where(savedCards.ContainsKey).Select(id => new
        {
            id,
            changes = StateChanges.Diff(
                new JsonObject { ["tasks"] = !suppliedValues.Contains(id) && original.TryGetValue(id, out var old) ? new JsonArray(old!.DeepClone()) : new JsonArray() },
                new JsonObject { ["tasks"] = new JsonArray(savedCards[id]!.DeepClone()) })
        }).ToArray();
        var kept = WorkspaceJson.Items(saved, "tasks").Select(c => WorkspaceJson.Text(c, "id")).ToHashSet();
        var removed = scopeIds.Where(id => !kept.Contains(id)).ToArray();
        before.State["tasks"] = new JsonArray();
        saved["tasks"] = new JsonArray();
        return new { version = saved["version"]!.GetValue<long>(), changes = StateChanges.Diff(before.State, saved), cards = updated, removed };
    }

    private static void ReconcileReferences(JsonObject before, JsonObject next, HashSet<string> edited)
    {
        string Rename(string collection, string name, string? project = null)
        {
            var old = WorkspaceJson.Items(before, collection).FirstOrDefault(d => WorkspaceJson.Text(d, "name") == name
                && (project is null || WorkspaceJson.Text(d, "project") == project));
            var replacement = WorkspaceJson.Items(next, collection).FirstOrDefault(d => WorkspaceJson.Text(d, "id") == WorkspaceJson.Text(old, "id"));
            return replacement is null ? name : WorkspaceJson.Text(replacement, "name");
        }
        var members = WorkspaceJson.Items(next, "members").ToDictionary(m => WorkspaceJson.Text(m, "email"), m => WorkspaceJson.Text(m, "name"));
        var previousMembers = WorkspaceJson.Items(before, "members").ToDictionary(m => WorkspaceJson.Text(m, "name"), m => WorkspaceJson.Text(m, "email"));
        var projects = WorkspaceJson.Items(next, "projects").Select(p => WorkspaceJson.Text(p, "id")).ToHashSet();
        var cards = WorkspaceJson.Items(next, "tasks");
        foreach (var card in cards.ToArray())
        {
            var project = WorkspaceJson.Text(card, "project");
            if (!projects.Contains(project)) { cards.Remove(card); continue; }
            if (edited.Contains(WorkspaceJson.Text(card, "id"))) continue;
            card!["status"] = Rename("statuses", WorkspaceJson.Text(card, "status"));
            card["bucket"] = Rename("buckets", WorkspaceJson.Text(card, "bucket"));
            card["swimlane"] = Rename("swimlanes", WorkspaceJson.Text(card, "swimlane"), project);
            card["labels"] = new JsonArray(WorkspaceJson.Items(card, "labels").Select(l => (JsonNode?)JsonValue.Create(Rename("labels", l!.GetValue<string>()))).ToArray());
            card["assignees"] = new JsonArray(WorkspaceJson.Items(card, "assignees").Select(a => a!.GetValue<string>())
                .Select(name => previousMembers.TryGetValue(name, out var email) ? members.GetValueOrDefault(email) : name)
                .Where(name => name is not null).Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        }
    }
}
