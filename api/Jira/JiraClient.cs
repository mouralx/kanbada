using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Kanbada.Api;

public class JiraSyncException(string message) : Exception(message);
public sealed class JiraHttpException(System.Net.HttpStatusCode status)
    : JiraSyncException($"Jira returned HTTP {(int)status}. Check credentials, JQL, field mappings, permissions, required fields and rate limits. No automatic HTTP write retry was attempted.")
{
    public System.Net.HttpStatusCode Status { get; } = status;
}
public sealed record JiraIssue(string Id, string Key, JsonObject Fields);

public sealed class JiraClient(HttpClient http, JiraSecrets secrets, JiraDestinationPolicy policy)
{
    private static readonly string[] Fields = ["summary", "description", "status", "priority", "labels", "duedate", "project", "assignee"];

    public static string AssigneeId(JsonObject assignee, string edition) =>
        (edition == "cloud" ? assignee["accountId"] : assignee["key"] ?? assignee["name"])?.GetValue<string>()
            ?? throw new JiraSyncException("Jira returned an assignee without an account ID or user key.");

    public async Task<string> BrowseUrl(JiraConnectionEntity connection, string key, CancellationToken ct)
    {
        var root = JiraDestinationPolicy.Parse(connection.BaseUrl);
        if (connection.Edition == "cloud" && root.Host == "api.atlassian.com")
        {
            var info = await Send(connection, HttpMethod.Get, "serverInfo", null, ct);
            root = JiraDestinationPolicy.Parse(info["baseUrl"]?.GetValue<string>()
                ?? throw new JiraSyncException("Jira did not return its browser URL."));
        }
        return new Uri(root, "browse/" + Uri.EscapeDataString(key)).AbsoluteUri;
    }

    private async Task<JsonObject> Send(JiraConnectionEntity c, HttpMethod method, string path, object? body, CancellationToken ct) =>
        (await SendNode(c, method, path, body, ct)).AsObject();

    private async Task<JsonNode> SendNode(JiraConnectionEntity c, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var root = policy.Validate(c.BaseUrl, c.Edition);
        using var request = new HttpRequestMessage(method, new Uri(root, $"rest/api/{(c.Edition == "cloud" ? "3" : "2")}/{path}"));
        var token = secrets.Unprotect(c.ProtectedToken);
        request.Headers.Authorization = c.Edition == "cloud"
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(c.Email + ":" + token)))
            : new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new JiraHttpException(response.StatusCode);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return new JsonObject();
        return await response.Content.ReadFromJsonAsync<JsonNode>(ct) ?? throw new JiraSyncException("Jira returned an empty response.");
    }

    public async Task<List<JiraIssue>> Search(JiraConnectionEntity c, CancellationToken ct)
    {
        var result = new List<JiraIssue>();
        var seen = new HashSet<string>();
        var tokens = new HashSet<string>();
        string? next = null;
        var start = 0;
        while (true)
        {
            object body = c.Edition == "cloud"
                ? new { jql = c.Jql, fields = Fields, maxResults = 100, nextPageToken = next }
                : new { jql = c.Jql, fields = Fields, maxResults = 100, startAt = start };
            var page = await Send(c, HttpMethod.Post, c.Edition == "cloud" ? "search/jql" : "search", body, ct);
            var issues = page["issues"]?.AsArray() ?? throw new JiraSyncException("Jira search did not return issues.");
            foreach (var node in issues)
            {
                var issue = Parse(node);
                if (seen.Add(issue.Id)) result.Add(issue);
            }
            if (result.Count > 10000) throw new JiraSyncException("JQL returned more than 10000 issues. Narrow the query; no items were synchronized.");
            if (c.Edition == "cloud")
            {
                if (page["isLast"]?.GetValue<bool>() == true) break;
                next = page["nextPageToken"]?.GetValue<string>();
                if (page["isLast"]?.GetValue<bool>() == false && string.IsNullOrEmpty(next))
                    throw new JiraSyncException("Jira search did not return the next page token.");
                if (string.IsNullOrEmpty(next)) break;
                if (!tokens.Add(next)) throw new JiraSyncException("Jira repeated a search page token.");
            }
            else
            {
                start += issues.Count;
                if (start >= (page["total"]?.GetValue<int>() ?? throw new JiraSyncException("Jira search did not return a total."))) break;
            }
            if (issues.Count == 0) throw new JiraSyncException("Jira search ended before all results were returned.");
        }
        return result;
    }

    public async Task<JiraIssue> Get(JiraConnectionEntity c, string id, CancellationToken ct) =>
        Parse(await Send(c, HttpMethod.Get, "issue/" + Uri.EscapeDataString(id) + "?fields=" + string.Join(',', Fields), null, ct));

    public async Task<object> Metadata(JiraConnectionEntity c, CancellationToken ct)
    {
        // These discovery resources return arrays rather than issue objects.
        var types = await SendArray(c, "project/" + Uri.EscapeDataString(c.JiraProjectKey) + "/statuses", ct);
        var priorities = c.Edition == "cloud" ? await CloudPriorities(c, ct) : await SendArray(c, "priority", ct);
        var issues = await Search(c, ct);
        var statuses = types.SelectMany(t => t!["statuses"]!.AsArray())
            .Concat(issues.Select(i => i.Fields["status"]).Where(s => s is not null))
            .GroupBy(s => Required(s, "id")).ToDictionary(g => g.Key, g => Required(g.First(), "name"));
        var assignees = issues.Where(i => i.Fields["assignee"] is JsonObject)
            .Select(i => i.Fields["assignee"]!.AsObject()).GroupBy(a => AssigneeId(a, c.Edition))
            .ToDictionary(g => g.Key, g => Required(g.First(), "displayName"));
        var warnings = new HashSet<string>();
        foreach (var mapping in c.Mappings.Where(m => m.Kind is "status" or "assignee"))
        {
            var choices = mapping.Kind == "status" ? statuses : assignees;
            if (choices.ContainsKey(mapping.JiraValue)) continue;
            var path = mapping.Kind == "status" ? "status/" + Uri.EscapeDataString(mapping.JiraValue)
                : "user?" + (c.Edition == "cloud" ? "accountId=" : "key=") + Uri.EscapeDataString(mapping.JiraValue);
            try
            {
                JsonObject choice;
                try { choice = await Send(c, HttpMethod.Get, path, null, ct); }
                catch (JiraHttpException error) when (error.Status == System.Net.HttpStatusCode.NotFound
                    && mapping.Kind == "assignee" && c.Edition == "data-center")
                {
                    choice = await Send(c, HttpMethod.Get, "user?username=" + Uri.EscapeDataString(mapping.JiraValue), null, ct);
                }
                choices[mapping.JiraValue] = Required(choice, mapping.Kind == "status" ? "name" : "displayName");
            }
            catch (JiraHttpException error) when (error.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
            {
                warnings.Add("A saved Jira choice is no longer accessible. Select a replacement or remove its mapping.");
            }
        }
        return new
        {
            IssueTypes = types.Select(t => new { Id = t!["id"]!.GetValue<string>(), Name = t["name"]!.GetValue<string>() }),
            Statuses = statuses.Select(s => new { Id = s.Key, Name = s.Value }).OrderBy(s => s.Name),
            Priorities = priorities.Select(p => new { Id = p!["id"]!.GetValue<string>(), Name = p["name"]!.GetValue<string>() }),
            Assignees = assignees.Select(a => new { Id = a.Key, Name = a.Value }).OrderBy(a => a.Name),
            Warnings = warnings,
            MatchedIssues = issues.Count
        };
    }

    private async Task<JsonArray> CloudPriorities(JiraConnectionEntity c, CancellationToken ct)
    {
        var priorities = new JsonArray();
        var start = 0;
        while (true)
        {
            var page = await Send(c, HttpMethod.Get, $"priority/search?startAt={start}&maxResults=100", null, ct);
            var values = page["values"]?.AsArray() ?? throw new JiraSyncException("Jira returned invalid priorities.");
            foreach (var value in values) priorities.Add(value?.DeepClone());
            start += values.Count;
            if (page["isLast"]?.GetValue<bool>() == true || start >= page["total"]?.GetValue<int>()) return priorities;
            if (values.Count == 0 || start > 10000) throw new JiraSyncException("Jira priority pagination did not complete.");
        }
    }

    private async Task<JsonArray> SendArray(JiraConnectionEntity c, string path, CancellationToken ct)
    {
        var result = await SendNode(c, HttpMethod.Get, path, null, ct);
        return result.AsArray();
    }

    public async Task<(string Id, string Key)> Create(JiraConnectionEntity c, JiraSnapshot snapshot, CancellationToken ct)
    {
        var fields = WriteFields(c, snapshot);
        fields["project"] = new JsonObject { ["key"] = c.JiraProjectKey };
        fields["issuetype"] = new JsonObject { ["id"] = c.IssueTypeId };
        var result = await Send(c, HttpMethod.Post, "issue", new { fields }, ct);
        return (Required(result, "id"), Required(result, "key"));
    }

    public async Task Update(JiraConnectionEntity c, JiraIssue current, JiraSnapshot snapshot, CancellationToken ct)
    {
        var fields = WriteFields(c, snapshot);
        // Do not replace existing rich text when the plain-text description did not change.
        if (JiraSnapshot.Description(current.Fields["description"]) == snapshot.DescriptionText)
            fields.Remove("description");
        await Send(c, HttpMethod.Put, "issue/" + Uri.EscapeDataString(current.Id), new { fields }, ct);
        await Transition(c, current, snapshot.Status, ct);
    }

    public async Task Transition(JiraConnectionEntity c, JiraIssue current, string status, CancellationToken ct)
    {
        var currentStatus = current.Fields["status"]?["id"]?.GetValue<string>();
        if (c.Mappings.Any(m => m.Kind == "status" && m.KanbadaValue == status && m.JiraValue == currentStatus)) return;
        var target = JiraSnapshot.ToJira(c, "status", status);
        if (current.Fields["status"]?["id"]?.GetValue<string>() == target) return;
        var path = "issue/" + Uri.EscapeDataString(current.Id) + "/transitions";
        var data = await Send(c, HttpMethod.Get, path, null, ct);
        var transitions = data["transitions"]?.AsArray()
            .Where(t => t?["to"]?["id"]?.GetValue<string>() == target).ToList() ?? [];
        if (transitions.Count != 1)
            throw new JiraSyncException("The mapped Jira status requires exactly one available direct transition. Check the workflow and transition permissions.");
        await Send(c, HttpMethod.Post, path, new { transition = new { id = Required(transitions[0], "id") } }, ct);
    }

    public static void ValidateWrite(JiraConnectionEntity c, JiraSnapshot s)
    {
        if (s.Title.Length is < 1 or > 255) throw new JiraSyncException("Jira summaries must contain 1 to 255 characters.");
        if (s.Labels.Any(l => l.Any(char.IsWhiteSpace)))
            throw new JiraSyncException("Jira labels cannot contain whitespace. Rename the corresponding Kanbada labels.");
        JiraSnapshot.ToJira(c, "status", s.Status);
        JiraSnapshot.ToJira(c, "priority", s.Priority);
    }

    private static JsonObject WriteFields(JiraConnectionEntity c, JiraSnapshot s)
    {
        ValidateWrite(c, s);
        return new JsonObject
        {
            ["summary"] = s.Title,
            ["description"] = c.Edition == "cloud" ? ToDocument(s.DescriptionText) : JsonValue.Create(s.DescriptionText),
            ["priority"] = new JsonObject { ["id"] = JiraSnapshot.ToJira(c, "priority", s.Priority) },
            ["labels"] = new JsonArray(s.Labels.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()),
            ["duedate"] = s.Due?.ToString("yyyy-MM-dd")
        };
    }

    public static JsonObject ToDocument(string text) => new()
    {
        ["type"] = "doc",
        ["version"] = 1,
        ["content"] = new JsonArray(text.Split('\n').Select(line => (JsonNode)new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = line.Length == 0 ? new JsonArray() : new JsonArray(new JsonObject { ["type"] = "text", ["text"] = line })
        }).ToArray())
    };

    private static JiraIssue Parse(JsonNode? node) => new(Required(node, "id"), Required(node, "key"),
        node?["fields"]?.AsObject() ?? throw new JiraSyncException("Jira issue fields are missing."));
    private static string Required(JsonNode? node, string name) =>
        node?[name]?.GetValue<string>() ?? throw new JiraSyncException($"Jira response is missing {name}.");
}
