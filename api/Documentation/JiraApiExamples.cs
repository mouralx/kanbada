using System.Text.Json.Nodes;

namespace Kanbada.Api;

public static class JiraApiExamples
{
    public static JsonObject Input() => ApiExamples.Json(new
    {
        version = 0,
        baseUrl = "https://example.atlassian.net",
        edition = "cloud",
        email = "alex@example.test",
        token = "REPLACE-WITH-YOUR-API-TOKEN",
        jql = "project = TEAM ORDER BY key",
        jiraProjectKey = "TEAM",
        issueTypeId = "10001",
        direction = "bidirectional",
        cron = "*/15 * * * *",
        timeZone = "Europe/Lisbon",
        enabled = false,
        syncAssignees = false,
        importMissingAssignees = false,
        mappings = new[]
        {
            new { kind = "status", kanbadaValue = "backlog", jiraValue = "10000", isDefault = true },
            new { kind = "status", kanbadaValue = "backlog", jiraValue = "10001", isDefault = false },
            new { kind = "priority", kanbadaValue = "Low", jiraValue = "4", isDefault = true },
            new { kind = "priority", kanbadaValue = "Medium", jiraValue = "3", isDefault = true },
            new { kind = "priority", kanbadaValue = "High", jiraValue = "2", isDefault = true }
        }
    }).AsObject();

    public static JsonObject Connection()
    {
        var result = Input();
        result.Remove("token");
        result["version"] = 1;
        result["id"] = "73716be0-a56b-4589-a5f6-2bf96a531132";
        result["hasToken"] = true;
        result["nextRunAt"] = "2026-09-25T10:00:00Z";
        result["requestedAt"] = null;
        result["lastStartedAt"] = null;
        result["lastFinishedAt"] = null;
        result["lastError"] = null;
        result["lastSyncedCount"] = 0;
        result["problems"] = new JsonArray();
        return result;
    }
}
