using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Kanbada.Api;

public sealed class GitHubClient(HttpClient http, GitHubSecrets secrets, GitHubDestinationPolicy policy)
{
    public const string Unset = "__none__";
    private const string PageInfo = "pageInfo { hasNextPage endCursor }";
    private const string FieldSelection = """
        nodes { ... on ProjectV2FieldCommon { id name dataType }
          ... on ProjectV2SingleSelectField { options { id name } } }
        pageInfo { hasNextPage endCursor }
        """;
    private const string ValuesSelection = """
        nodes {
          ... on ProjectV2ItemFieldSingleSelectValue { optionId field { ... on ProjectV2FieldCommon { id } } }
          ... on ProjectV2ItemFieldDateValue { date field { ... on ProjectV2FieldCommon { id } } }
        } pageInfo { hasNextPage endCursor }
        """;
    private const string ItemSelection = """
        id isArchived
        fieldValues(first:100) {
          nodes {
            ... on ProjectV2ItemFieldSingleSelectValue { optionId field { ... on ProjectV2FieldCommon { id } } }
            ... on ProjectV2ItemFieldDateValue { date field { ... on ProjectV2FieldCommon { id } } }
          } pageInfo { hasNextPage endCursor }
        }
        content {
          __typename
          ... on DraftIssue { id title body assignees(first:100) { nodes { id } pageInfo { hasNextPage endCursor } } }
          ... on Issue { id title body url number repository { id nameWithOwner }
            labels(first:100) { nodes { id name } pageInfo { hasNextPage endCursor } }
            assignees(first:100) { nodes { id } pageInfo { hasNextPage endCursor } } }
          ... on PullRequest { id title body url number repository { id nameWithOwner }
            labels(first:100) { nodes { id name } pageInfo { hasNextPage endCursor } }
            assignees(first:100) { nodes { id } pageInfo { hasNextPage endCursor } } }
        }
        """;

    public async Task<GitHubMetadata> Metadata(GitHubConnectionEntity c, CancellationToken ct)
    {
        var ownerType = c.OwnerType == "organization" ? "organization" : "user";
        var data = await Send(c, $$"""
            query($owner:String!,$number:Int!) {
              owner:{{ownerType}}(login:$owner) { projectV2(number:$number) { id title url closed fields(first:100) { {{FieldSelection}} } } }
            }
            """, new { owner = c.Owner, number = c.ProjectNumber }, ct);
        var project = Object(data["owner"]?["projectV2"]);
        if (project["closed"]?.GetValue<bool>() != false) throw new GitHubSyncException("The GitHub project is closed or unavailable.");
        var id = Text(project, "id");
        var fields = new List<GitHubField>();
        var page = Object(project["fields"]);
        string? cursor = null;
        while (true)
        {
            foreach (var node in Nodes(page))
            {
                var field = Object(node);
                fields.Add(new GitHubField(Text(field, "id"), Text(field, "name"), Text(field, "dataType"),
                    field["options"]?.AsArray().Select(o => new GitHubOption(Text(Object(o), "id"), Text(Object(o), "name"))).ToList() ?? []));
            }
            var next = Next(page, cursor);
            if (next is null) break;
            cursor = next;
            data = await Send(c, $$"""
                query($id:ID!,$after:String!) { node(id:$id) { ... on ProjectV2 { fields(first:100,after:$after) { {{FieldSelection}} } } } }
                """, new { id, after = cursor }, ct);
            page = Object(data["node"]?["fields"]);
        }
        var repositoryId = "";
        if (c.Repository.Length > 0)
        {
            var parts = c.Repository.Split('/');
            data = await Send(c, "query($owner:String!,$name:String!) { repository(owner:$owner,name:$name) { id isArchived hasIssuesEnabled } }",
                new { owner = parts[0], name = parts[1] }, ct);
            var repository = Object(data["repository"]);
            repositoryId = Text(repository, "id");
            if (c.Direction != "github-to-kanbada" && (repository["isArchived"]?.GetValue<bool>() != false || repository["hasIssuesEnabled"]?.GetValue<bool>() != true))
                throw new GitHubSyncException("The target repository must be unarchived and have issues enabled.");
        }
        return new GitHubMetadata(id, Text(project, "title"), GitHubDestinationPolicy.BrowserUrl(c.BaseUrl, Text(project, "url")), repositoryId, fields);
    }

    public async Task<GitHubOption> User(GitHubConnectionEntity c, string login, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(login, "^[A-Za-z0-9][A-Za-z0-9-]{0,99}$"))
            throw new ApiError(400, "Enter a GitHub username.");
        var data = await Send(c, "query($login:String!) { user(login:$login) { id login } }", new { login }, ct);
        return new GitHubOption(Text(Object(data["user"]), "id"), Text(Object(data["user"]), "login"));
    }

    public async IAsyncEnumerable<GitHubItem> Items(GitHubConnectionEntity c, [EnumeratorCancellation] CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var data = await Send(c, $$"""
                query($id:ID!,$after:String) { node(id:$id) { ... on ProjectV2 {
                  items(first:50,after:$after) { nodes { {{ItemSelection}} } {{PageInfo}} }
                } } }
                """, new { id = c.RemoteProjectId, after = cursor }, ct);
            var page = Object(data["node"]?["items"]);
            foreach (var node in Nodes(page)) yield return await ParseItem(c, Object(node), ct);
            cursor = Next(page, cursor);
        } while (cursor is not null);
    }

    public async Task<GitHubItem> Item(GitHubConnectionEntity c, string id, CancellationToken ct)
    {
        var data = await Send(c, $$"""
            query($id:ID!) { node(id:$id) { ... on ProjectV2Item { project { id } {{ItemSelection}} } } }
            """, new { id }, ct);
        var item = Object(data["node"]);
        if (Text(Object(item["project"]), "id") != c.RemoteProjectId)
            throw new GitHubSyncException("The item is no longer in the configured GitHub project.");
        return await ParseItem(c, item, ct);
    }

    private async Task<GitHubItem> ParseItem(GitHubConnectionEntity c, JsonObject item, CancellationToken ct)
    {
        var id = Text(item, "id");
        var archived = item["isArchived"]?.GetValue<bool>() ?? throw new GitHubSyncException("GitHub omitted the item archive state.");
        if (archived || item["content"] is not JsonObject content || content["__typename"]?.GetValue<string>() is not ("Issue" or "PullRequest" or "DraftIssue"))
            return new GitHubItem(id, archived, null, []);
        var values = new Dictionary<string, string>();
        var page = Object(item["fieldValues"]);
        string? cursor = null;
        while (true)
        {
            foreach (var value in Nodes(page).OfType<JsonObject>())
                if (value["field"] is JsonObject field)
                    values[Text(field, "id")] = value["optionId"]?.GetValue<string>() ?? value["date"]?.GetValue<string>() ?? Unset;
            var next = Next(page, cursor);
            if (next is null) break;
            cursor = next;
            var data = await Send(c, $$"""
                query($id:ID!,$after:String!) { node(id:$id) { ... on ProjectV2Item { fieldValues(first:100,after:$after) { {{ValuesSelection}} } } } }
                """, new { id, after = cursor }, ct);
            page = Object(data["node"]?["fieldValues"]);
        }
        var type = Text(content, "__typename");
        var contentId = Text(content, "id");
        var assignees = await ContentValues(c, content, type, "assignees", "id", ct);
        var labels = type == "DraftIssue" ? null : await ContentValues(c, content, type, "labels", "name", ct);
        var repository = type == "DraftIssue" ? null : Object(content["repository"]);
        var url = type == "DraftIssue" ? ProjectUrl(c) : GitHubDestinationPolicy.BrowserUrl(c.BaseUrl, Text(content, "url"));
        var key = type == "DraftIssue" ? "Draft item" : Text(repository!, "nameWithOwner") + "#" + content["number"]!.GetValue<int>();
        return new GitHubItem(id, false, new GitHubContent(contentId, type, Text(content, "title"), Text(content, "body"), url, key,
            repository is null ? null : Text(repository, "id"), labels, assignees), values);
    }

    private async Task<string[]> ContentValues(GitHubConnectionEntity c, JsonObject content, string type, string connection, string value, CancellationToken ct)
    {
        var values = new List<string>();
        var page = Object(content[connection]);
        string? cursor = null;
        while (true)
        {
            values.AddRange(Nodes(page).Select(n => Text(Object(n), value)));
            var next = Next(page, cursor);
            if (next is null) break;
            cursor = next;
            var data = await Send(c, $$"""
                query($id:ID!,$after:String!) { node(id:$id) { ... on {{type}} {
                  {{connection}}(first:100,after:$after) { nodes { {{value}} } {{PageInfo}} }
                } } }
                """, new { id = Text(content, "id"), after = cursor }, ct);
            page = Object(data["node"]?[connection]);
        }
        return values.Distinct().ToArray();
    }

    public static string ProjectUrl(GitHubConnectionEntity c) => new Uri(new Uri(c.BaseUrl),
        $"{(c.OwnerType == "organization" ? "orgs" : "users")}/{Uri.EscapeDataString(c.Owner)}/projects/{c.ProjectNumber}").AbsoluteUri;

    public async Task<string[]> LabelIds(GitHubConnectionEntity c, string repositoryId, string[] names, CancellationToken ct)
    {
        var wanted = names.Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(n => n, _ => "", StringComparer.OrdinalIgnoreCase);
        string? cursor = null;
        while (wanted.Values.Any(v => v.Length == 0))
        {
            var data = await Send(c, $$"""
                query($id:ID!,$after:String) { node(id:$id) { ... on Repository { labels(first:100,after:$after) { nodes { id name } {{PageInfo}} } } } }
                """, new { id = repositoryId, after = cursor }, ct);
            var page = Object(data["node"]?["labels"]);
            foreach (var node in Nodes(page))
            {
                var label = Object(node);
                var name = Text(label, "name").Trim();
                if (wanted.TryGetValue(name, out var existing) && existing.Length == 0) wanted[name] = Text(label, "id");
            }
            cursor = Next(page, cursor);
            if (cursor is null) break;
        }
        foreach (var name in wanted.Where(x => x.Value.Length == 0).Select(x => x.Key).ToArray())
            wanted[name] = await Mutate(c, "createLabel", "CreateLabelInput", "label", new { repositoryId, name, color = "879eb9" }, ct);
        return wanted.Values.ToArray();
    }

    public Task<string> CreateIssue(GitHubConnectionEntity c, GitHubSnapshot snapshot, string[]? labelIds, CancellationToken ct)
    {
        var input = new JsonObject { ["repositoryId"] = c.RepositoryId, ["title"] = snapshot.Title, ["body"] = snapshot.Body };
        if (labelIds is not null) input["labelIds"] = new JsonArray(labelIds.Select(x => JsonValue.Create(x)).ToArray());
        return Mutate(c, "createIssue", "CreateIssueInput", "issue", input, ct);
    }

    public Task<string> AddItem(GitHubConnectionEntity c, string contentId, CancellationToken ct) =>
        Mutate(c, "addProjectV2ItemById", "AddProjectV2ItemByIdInput", "item", new { projectId = c.RemoteProjectId, contentId }, ct);

    public async Task Update(GitHubConnectionEntity c, GitHubItem item, GitHubSnapshot snapshot, CancellationToken ct)
    {
        GitHubSnapshot.ValidateWrite(c, snapshot);
        var content = item.Content ?? throw new GitHubSyncException("GitHub content is inaccessible.");
        var (mutation, inputType, result, idField) = content.Type switch
        {
            "Issue" => ("updateIssue", "UpdateIssueInput", "issue", "id"),
            "PullRequest" => ("updatePullRequest", "UpdatePullRequestInput", "pullRequest", "pullRequestId"),
            "DraftIssue" => ("updateProjectV2DraftIssue", "UpdateProjectV2DraftIssueInput", "draftIssue", "draftIssueId"),
            _ => throw new GitHubSyncException("Unsupported GitHub content type.")
        };
        var input = new JsonObject { [idField] = content.Id, ["title"] = snapshot.Title, ["body"] = snapshot.Body };
        if (snapshot.Labels is not null && content.RepositoryId is not null)
            input["labelIds"] = new JsonArray((await LabelIds(c, content.RepositoryId, snapshot.Labels, ct)).Select(x => JsonValue.Create(x)).ToArray());
        await Mutate(c, mutation, inputType, result, input, ct);
        await WriteFields(c, item.Id, snapshot, ct);
    }

    public async Task WriteFields(GitHubConnectionEntity c, string itemId, GitHubSnapshot snapshot, CancellationToken ct)
    {
        await Field(c, itemId, c.StatusFieldId, "singleSelectOptionId", GitHubSnapshot.ToGitHub(c, "status", snapshot.Status), ct);
        if (c.PriorityFieldId.Length > 0) await Field(c, itemId, c.PriorityFieldId, "singleSelectOptionId", GitHubSnapshot.ToGitHub(c, "priority", snapshot.Priority!), ct);
        if (c.DueFieldId.Length > 0) await Field(c, itemId, c.DueFieldId, "date", snapshot.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ct);
    }

    private async Task Field(GitHubConnectionEntity c, string itemId, string fieldId, string kind, string? value, CancellationToken ct)
    {
        if (value is null or Unset)
            await Mutate(c, "clearProjectV2ItemFieldValue", "ClearProjectV2ItemFieldValueInput", "projectV2Item",
                new { projectId = c.RemoteProjectId, itemId, fieldId }, ct);
        else
            await Mutate(c, "updateProjectV2ItemFieldValue", "UpdateProjectV2ItemFieldValueInput", "projectV2Item",
                new { projectId = c.RemoteProjectId, itemId, fieldId, value = new JsonObject { [kind] = value } }, ct);
    }

    public async Task<string> ResolveIssue(GitHubConnectionEntity c, string issueUrl, CancellationToken ct)
    {
        var url = new Uri(GitHubDestinationPolicy.BrowserUrl(c.BaseUrl, issueUrl));
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 4 || parts[2] != "issues" || !int.TryParse(parts[3], out var number) || number < 1
            || !string.Equals(parts[0] + "/" + parts[1], c.Repository, StringComparison.OrdinalIgnoreCase))
            throw new ApiError(400, "Use an issue URL in the configured outbound repository.");
        var data = await Send(c, "query($owner:String!,$name:String!,$number:Int!) { repository(owner:$owner,name:$name) { issue(number:$number) { id } } }",
            new { owner = parts[0], name = parts[1], number }, ct);
        return Text(Object(data["repository"]?["issue"]), "id");
    }

    private async Task<string> Mutate(GitHubConnectionEntity c, string mutation, string inputType, string result, object input, CancellationToken ct)
    {
        var data = await Send(c, $"mutation($input:{inputType}!) {{ {mutation}(input:$input) {{ {result} {{ id }} }} }}", new { input }, ct);
        return Text(Object(data[mutation]?[result]), "id");
    }

    private async Task<JsonObject> Send(GitHubConnectionEntity c, string query, object variables, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, policy.Api(c.BaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.Unprotect(c.ProtectedToken));
        request.Content = JsonContent.Create(new { query, variables });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        DateTimeOffset? retry = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is TimeSpan delay ? DateTimeOffset.UtcNow + delay : null);
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0"
            && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out var seconds)
            && seconds is >= 0 and <= 253402300799)
            retry = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (!response.IsSuccessStatusCode)
            throw new GitHubSyncException($"GitHub returned HTTP {(int)response.StatusCode}. Check the token, permissions, server access and rate limits.",
                retry ?? (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? DateTimeOffset.UtcNow.AddMinutes(15) : null));
        JsonObject json;
        try { json = Object(await response.Content.ReadFromJsonAsync<JsonObject>(ct)); }
        catch (System.Text.Json.JsonException) { throw new GitHubSyncException("GitHub returned an invalid GraphQL response."); }
        if (json["errors"] is JsonArray errors && errors.Count > 0)
        {
            if (errors.Any(e => e?["type"]?.GetValue<string>() == "RATE_LIMITED"))
                throw new GitHubSyncException("GitHub rate limit reached. Synchronization will resume after the limit resets.", retry ?? DateTimeOffset.UtcNow.AddMinutes(15));
            throw new GitHubSyncException("GitHub rejected a GraphQL request. Check token permissions, item access, field mappings and Projects v2 support.", retry);
        }
        return Object(json["data"]);
    }

    private static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new GitHubSyncException("GitHub returned missing or inaccessible data.");
    private static string Text(JsonObject node, string key) => node[key]?.GetValue<string>() ?? throw new GitHubSyncException($"GitHub omitted the required {key} field.");
    private static JsonArray Nodes(JsonObject page) => page["nodes"] as JsonArray ?? throw new GitHubSyncException("GitHub omitted a result page.");
    private static string? Next(JsonObject page, string? previous)
    {
        var info = Object(page["pageInfo"]);
        if (info["hasNextPage"]?.GetValue<bool>() == false) return null;
        var cursor = Text(info, "endCursor");
        if (cursor.Length == 0 || cursor == previous) throw new GitHubSyncException("GitHub returned an invalid pagination cursor.");
        return cursor;
    }
}
