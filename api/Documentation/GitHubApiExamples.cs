using System.Text.Json.Nodes;

namespace Kanbada.Api;

public static class GitHubApiExamples
{
    public static JsonNode Input() => ApiExamples.Json(new
    {
        version = 0,
        baseUrl = "https://github.com",
        owner = "example-org",
        ownerType = "organization",
        projectNumber = 1,
        repository = "example-org/example-repo",
        token = "REPLACE-WITH-YOUR-TOKEN",
        direction = "github-to-kanbada",
        statusFieldId = "PVTSSF_example",
        priorityFieldId = "",
        dueFieldId = "",
        syncLabels = true,
        syncAssignees = false,
        cron = "*/15 * * * *",
        timeZone = "UTC",
        enabled = false,
        mappings = new[] { new { kind = "status", kanbadaValue = "backlog", gitHubValue = "option-id-from-test", isDefault = true } }
    });

    public static JsonNode Connection(bool enabled = true, long version = 1) => ApiExamples.Json(new
    {
        id = "73716be0-a56b-4589-a5f6-2bf96a531132",
        version,
        baseUrl = "https://github.com/",
        owner = "example-org",
        ownerType = "organization",
        projectNumber = 1,
        repository = "example-org/example-repo",
        hasToken = true,
        direction = "github-to-kanbada",
        enabled,
        lastSyncedCount = 0,
        lastError = (string?)null,
        problems = Array.Empty<object>()
    });
}
