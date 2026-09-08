using System.Text.Json.Nodes;

namespace Kanbada.Api;

public static class CardHistory
{
    public static JsonArray Record(JsonNode? before, JsonNode task, string actor)
    {
        var history = before?["history"]?.DeepClone() as JsonArray ?? new JsonArray();
        var changes = new JsonArray();
        if (before is null)
            changes.Add("Created card in " + WorkspaceJson.Text(task, "status"));
        else
            foreach (var field in new[]
            {
                "title",
                "description",
                "status",
                "priority",
                "due",
                "bucket",
                "swimlane",
                "labels",
                "assignees",
                "comments",
                "checklist",
                "attachments"
            }

            )
                if (!JsonNode.DeepEquals(before[field], task[field]))
                    changes.Add(field == "status" ? "Status: " + WorkspaceJson.Text(before, field) + " → " + WorkspaceJson.Text(task, field) : "Updated " + field);
        if (changes.Count > 0)
            history.Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["at"] = DateTimeOffset.UtcNow.ToString("O"), ["actor"] = actor, ["changes"] = changes });
        return history;
    }
}
