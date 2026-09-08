using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kanbada.Api;

public static class WorkspaceValidator
{
    public static void Validate(JsonObject state, bool personal)
    {
        try
        {
            ValidateState(state, personal);
        }
        catch (InvalidOperationException)
        {
            throw new ApiError(400, "Workspace fields have invalid JSON types.");
        }
    }

    private static void ValidateState(JsonObject state, bool personal)
    {
        foreach (var field in new[]
        {
            "tasks",
            "projects",
            "statuses",
            "buckets",
            "labels",
            "swimlanes",
            "members",
            "notifications",
            "activity"
        }

        )
            if (state[field] is not JsonArray)
                throw new ApiError(400, "Missing collection: " + field);
        if (state["workspace"] is not JsonObject)
            throw new ApiError(400, "Workspace is required.");
        if (personal && WorkspaceJson.Text(state["workspace"], "name") != "My Workspace")
            throw new ApiError(400, "My Workspace cannot be renamed.");
        if (WorkspaceJson.Text(state["workspace"], "name").Trim().Length is < 1 or > 80)
            throw new ApiError(400, "Invalid workspace name.");
        var projects = WorkspaceJson.Items(state, "projects");
        var permanent = projects.Where(p => WorkspaceJson.Text(p, "id") == "my-activities").ToArray();
        if (permanent.Length != 1 || WorkspaceJson.Text(permanent[0], "name") != "My activities" || permanent[0]?["archived"]?.GetValue<bool>() == true || WorkspaceJson.Text(permanent[0], "system") != "activities")
            throw new ApiError(400, "My activities cannot be renamed, archived, or deleted.");
        foreach (var field in new[]
        {
            "projects",
            "statuses",
            "buckets",
            "labels",
            "swimlanes"
        }

        )
        {
            var ids = new HashSet<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in WorkspaceJson.Items(state, field))
            {
                if (item is not JsonObject || WorkspaceJson.Text(item, "id").Length == 0 || !ids.Add(WorkspaceJson.Text(item, "id")) || WorkspaceJson.Text(item, "name").Trim().Length is < 1 or > 120 || !names.Add((field == "swimlanes" ? WorkspaceJson.Text(item, "project") + "/" : "") + WorkspaceJson.Text(item, "name").Trim()))
                    throw new ApiError(400, "Invalid or duplicate " + field);
                if (!Regex.IsMatch(WorkspaceJson.Text(item, "color"), "^#[a-fA-F0-9]{6}$"))
                    throw new ApiError(400, "Invalid color.");
            }
        }

        if (WorkspaceJson.Items(state, "statuses").Count == 0)
            throw new ApiError(400, "At least one status is required.");
        var namesMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emailMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in WorkspaceJson.Items(state, "members"))
        {
            var email = WorkspaceJson.Text(m, "email");
            if (!namesMembers.Add(WorkspaceJson.Text(m, "name").Trim()) || WorkspaceJson.Text(m, "name").Trim().Length is < 1 or > 120 || !emailMembers.Add(email) || !System.Net.Mail.MailAddress.TryCreate(email, out _))
                throw new ApiError(400, "Members need unique names and valid unique emails.");
        }

        foreach (var lane in WorkspaceJson.Items(state, "swimlanes"))
            if (!projects.Any(p => WorkspaceJson.Text(p, "id") == WorkspaceJson.Text(lane, "project")))
                throw new ApiError(400, "Unknown swimlane project.");
        foreach (var task in WorkspaceJson.Items(state, "tasks"))
        {
            foreach (var collection in new[] { "labels", "assignees", "comments", "checklist" })
                if (task?[collection] is not JsonArray) throw new ApiError(400, "Cards require an array for " + collection);
            if (task?["attachments"] is not null && task["attachments"] is not JsonArray)
                throw new ApiError(400, "Card attachments must be an array.");
            foreach (var collection in new[] { "labels", "assignees", "comments" })
                foreach (var value in WorkspaceJson.Items(task, collection))
                    if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out _))
                        throw new ApiError(400, "Card " + collection + " must contain strings.");
            foreach (var item in WorkspaceJson.Items(task, "checklist"))
                if (item is not JsonObject || string.IsNullOrWhiteSpace(WorkspaceJson.Text(item, "text")) || item["done"] is not JsonValue done || !done.TryGetValue<bool>(out _))
                    throw new ApiError(400, "Checklist items require text and a boolean done flag.");

            if (WorkspaceJson.Text(task, "title").Trim().Length is < 1 or > 500 || !projects.Any(p => WorkspaceJson.Text(p, "id") == WorkspaceJson.Text(task, "project")))
                throw new ApiError(400, "Cards require a title and valid project.");
            var due = WorkspaceJson.Text(task, "due");
            if (due.Length > 0 && !DateOnly.TryParseExact(due, "yyyy-MM-dd", out _))
                throw new ApiError(400, "Invalid due date.");
            if (!new[]
            {
                "Low",
                "Medium",
                "High"
            }.Contains(WorkspaceJson.Text(task, "priority")))
                throw new ApiError(400, "Invalid priority.");
            if (!WorkspaceJson.Items(state, "statuses").Any(s => WorkspaceJson.Text(s, "name") == WorkspaceJson.Text(task, "status")))
                throw new ApiError(400, "Unknown status.");
            if (WorkspaceJson.Text(task, "bucket") is { Length: > 0 } bucket && !WorkspaceJson.Items(state, "buckets").Any(b => WorkspaceJson.Text(b, "name") == bucket))
                throw new ApiError(400, "Unknown bucket.");
            if (WorkspaceJson.Text(task, "swimlane") is { Length: > 0 } swimlane && !WorkspaceJson.Items(state, "swimlanes").Any(l => WorkspaceJson.Text(l, "name") == swimlane && WorkspaceJson.Text(l, "project") == WorkspaceJson.Text(task, "project")))
                throw new ApiError(400, "Unknown swimlane.");
            foreach (var label in WorkspaceJson.Items(task, "labels"))
                if (!WorkspaceJson.Items(state, "labels").Any(l => WorkspaceJson.Text(l, "name") == label?.GetValue<string>()))
                    throw new ApiError(400, "Unknown label.");
        }
    }
}
