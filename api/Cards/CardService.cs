using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kanbada.Api;

public sealed record CreatedCard(JsonObject Card, long WorkspaceVersion);

/// <summary>Creates a card through the same authorized, versioned transaction as portal saves.</summary>
public sealed class CardService(WorkspaceStore workspaces)
{
    public async Task<CreatedCard> Create(Guid workspaceId, Guid userId, CreateCardRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new ApiError(400, "A card title is required.");

        var snapshot = await workspaces.Read(workspaceId, userId);
        var state = snapshot.State;
        var id = "KB-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var card = JsonSerializer.SerializeToNode(input, JsonSerializerOptions.Web)!.AsObject();
        card["id"] = id;
        card["title"] = input.Title.Trim();
        card["status"] = input.Status ?? WorkspaceJson.Text(WorkspaceJson.Items(state, "statuses")[0], "name");
        card["due"] = input.Due ?? "";
        foreach (var collection in new[] { "labels", "assignees", "comments", "checklist", "attachments" })
            card[collection] ??= new JsonArray();
        card["history"] = new JsonArray();
        WorkspaceJson.Items(state, "tasks").Add(card);

        // A competing workspace edit returns 409 instead of overwriting its changes.
        var saved = await workspaces.Save(workspaceId, userId, state, snapshot.Version);
        var result = WorkspaceJson.Items(saved, "tasks").First(task => WorkspaceJson.Text(task, "id") == id)!;
        return new CreatedCard(result.DeepClone().AsObject(), saved["version"]!.GetValue<long>());
    }
}
