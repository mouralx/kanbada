using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kanbada.Api;

/// <summary>Illustrative contract data only. Never reads a database or exposes real credentials.</summary>
public static class ApiExamples
{
    public const string WorkspaceId = "11111111-1111-4111-8111-111111111111";
    public const string UserId = "22222222-2222-4222-8222-222222222222";
    public const string FileId = "33333333-3333-4333-8333-333333333333";
    public const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string CardId = "KB-A1B2C3D4";

    public static JsonNode Json(object value) => JsonSerializer.SerializeToNode(value)!;

    public static JsonObject Attachment() => Json(new
    {
        id = FileId,
        name = "requirements.pdf",
        size = 24576,
        type = "application/pdf",
        addedAt = "2026-09-08T09:00:00Z"
    }).AsObject();

    public static JsonObject Card() => Json(new
    {
        id = CardId,
        project = "my-activities",
        title = "Review release requirements",
        description = "Agree on acceptance criteria before implementation.",
        status = "Backlog",
        priority = "High",
        due = "",
        bucket = "Discovery",
        swimlane = "Research",
        labels = new[] { "Product" },
        assignees = new[] { "Alex Morgan" },
        comments = new[] { "Please review the acceptance criteria." },
        checklist = new[] { new { text = "Confirm scope", done = false } },
        attachments = Array.Empty<object>(),
        history = Array.Empty<object>()
    }).AsObject();

    public static JsonObject Workspace()
    {
        var state = Initial.State("studio", "My Workspace", Guid.Parse(UserId), "Alex Morgan", "alex@example.test", true);
        state["version"] = 1;
        state["workspace"]!["canManage"] = true;
        state["members"]![0]!["invitationToken"] = null;
        state["buckets"] = Json(new[] { new { id = "bucket-discovery", name = "Discovery", color = "#849bb8", complete = false } });
        state["labels"] = Json(new[] { new { id = "label-product", name = "Product", color = "#9678b5", complete = false } });
        state["swimlanes"] = Json(new[] { new { id = "lane-research", name = "Research", color = "#718eb0", complete = false, project = "my-activities" } });
        state["tasks"] = new JsonArray(Card());
        return state;
    }

    public static JsonObject CreatedWorkspace()
    {
        var state = Initial.State(WorkspaceId, "Product delivery", Guid.Parse(UserId), "Alex Morgan", "alex@example.test", false);
        state["version"] = 1;
        state["workspace"]!["canManage"] = true;
        return state;
    }

    public static JsonObject SavedWorkspace()
    {
        var state = Workspace();
        state["version"] = 2;
        state["tasks"]![0]!["history"] = Json(new[] { new
        {
            id = "44444444-4444-4444-8444-444444444444", at = "2026-09-08T09:00:00Z",
            actor = "Alex Morgan", changes = new[] { "Created card in Backlog" }
        }});
        return state;
    }

    public static JsonObject Share() => Json(new
    {
        token = Token,
        workspaceId = "studio",
        cardId = CardId,
        access = "members",
        expiresAt = "2026-09-15T09:00:00Z",
        createdAt = "2026-09-08T09:00:00Z",
        sourcePrefix = ""
    }).AsObject();

    public static JsonObject SharedCard()
    {
        var workspace = Workspace();
        var share = Share();
        share["workspaceId"] = WorkspaceId;
        share["sourcePrefix"] = "share:" + Token;
        return new JsonObject
        {
            ["share"] = share,
            ["task"] = SavedWorkspace()["tasks"]![0]!.DeepClone(),
            ["data"] = new JsonObject
            {
                ["workspace"] = workspace["workspace"]!.DeepClone(),
                ["projects"] = workspace["projects"]!.DeepClone(),
                ["labels"] = workspace["labels"]!.DeepClone()
            }
        };
    }
}
