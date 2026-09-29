using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class FakeGitHub : HttpMessageHandler
{
    public Dictionary<string, JsonObject> Items { get; } = [];
    public Dictionary<string, JsonObject> Contents { get; } = [];
    public Dictionary<string, string> Labels { get; } = [];
    public Dictionary<string, JsonObject> Users { get; } = [];
    public List<string> Queries { get; } = [];
    public List<Uri> Destinations { get; } = [];
    public List<JsonObject> Mutations { get; } = [];
    public int PageSize { get; set; } = 2;
    public bool ManyFields { get; set; }
    public bool LoseCreateResponse { get; set; }
    public bool RejectCreateOnce { get; set; }
    public bool LoseAddResponse { get; set; }
    public bool RejectFieldOnce { get; set; }
    public bool RateLimited { get; set; }
    public int CreateCount { get; private set; }
    public Func<string, Task>? BeforeRequest { get; set; }

    public JsonObject Add(string id, string type = "Issue", string? title = null)
    {
        var content = new JsonObject
        {
            ["__typename"] = type,
            ["id"] = "CONTENT-" + id,
            ["title"] = title ?? "Item " + id,
            ["body"] = "Description\r\n",
            ["url"] = "https://github.com/acme/repo/" + (type == "PullRequest" ? "pull/" : "issues/") + (Contents.Count + 1),
            ["number"] = Contents.Count + 1,
            ["repository"] = new JsonObject { ["id"] = "REPO", ["nameWithOwner"] = "acme/repo" },
            ["labels"] = new JsonArray(),
            ["assignees"] = new JsonArray()
        };
        Contents.Add("CONTENT-" + id, content);
        var item = new JsonObject
        {
            ["id"] = id,
            ["isArchived"] = false,
            ["contentId"] = "CONTENT-" + id,
            ["values"] = new JsonArray(Value("STATUS", "todo"), Value("PRIORITY", "medium"), Value("DUE", "2026-10-10", date: true))
        };
        Items.Add(id, item);
        return item;
    }

    public JsonObject Content(string item) => Contents[Items[item]["contentId"]!.GetValue<string>()];
    public static JsonObject Value(string field, string value, bool date = false) =>
        new() { ["field"] = new JsonObject { ["id"] = field }, [date ? "date" : "optionId"] = value };

    private static JsonObject Page(IEnumerable<JsonNode?> nodes, string? after, int size)
    {
        var all = nodes.ToList();
        var offset = after is null ? 0 : int.Parse(after);
        var taken = all.Skip(offset).Take(size).Select(n => n?.DeepClone()).ToArray();
        var more = offset + taken.Length < all.Count;
        return new JsonObject
        {
            ["nodes"] = new JsonArray(taken),
            ["pageInfo"] = new JsonObject { ["hasNextPage"] = more, ["endCursor"] = more ? (offset + taken.Length).ToString() : null }
        };
    }

    private JsonObject Render(JsonObject item)
    {
        var result = new JsonObject
        {
            ["id"] = item["id"]!.DeepClone(),
            ["isArchived"] = item["isArchived"]!.DeepClone(),
            ["project"] = new JsonObject { ["id"] = "PROJECT" },
            ["fieldValues"] = Page(item["values"]!.AsArray(), null, 100)
        };
        if (item["contentId"] is null) { result["content"] = null; return result; }
        var content = Contents[item["contentId"]!.GetValue<string>()].DeepClone().AsObject();
        content["labels"] = Page(content["labels"]!.AsArray(), null, 100);
        content["assignees"] = Page(content["assignees"]!.AsArray(), null, 100);
        result["content"] = content;
        return result;
    }

    private JsonArray Fields()
    {
        var result = new JsonArray();
        if (ManyFields)
            for (var i = 0; i < 101; i++) result.Add(new JsonObject { ["id"] = "TEXT-" + i, ["name"] = "Text " + i, ["dataType"] = "TEXT" });
        result.Add(new JsonObject
        {
            ["id"] = "STATUS",
            ["name"] = "Status",
            ["dataType"] = "SINGLE_SELECT",
            ["options"] = new JsonArray(new JsonObject { ["id"] = "todo", ["name"] = "Todo" }, new JsonObject { ["id"] = "doing", ["name"] = "Doing" }, new JsonObject { ["id"] = "done", ["name"] = "Done" })
        });
        result.Add(new JsonObject
        {
            ["id"] = "PRIORITY",
            ["name"] = "Priority",
            ["dataType"] = "SINGLE_SELECT",
            ["options"] = new JsonArray(new JsonObject { ["id"] = "low", ["name"] = "Low" }, new JsonObject { ["id"] = "medium", ["name"] = "Medium" }, new JsonObject { ["id"] = "high", ["name"] = "High" })
        });
        result.Add(new JsonObject { ["id"] = "DUE", ["name"] = "Due", ["dataType"] = "DATE" });
        return result;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var input = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        var query = input["query"]!.GetValue<string>();
        var variables = input["variables"]!.AsObject();
        Queries.Add(query);
        Destinations.Add(request.RequestUri!);
        Xunit.Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        if (BeforeRequest is not null) await BeforeRequest(query);
        if (RateLimited)
        {
            var limited = Response(new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["type"] = "RATE_LIMITED", ["message"] = "secret-should-not-appear" }) });
            limited.Headers.Add("X-RateLimit-Remaining", "0");
            limited.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString());
            return limited;
        }
        var id = variables["id"]?.GetValue<string>();
        var after = variables["after"]?.GetValue<string>();
        JsonObject data;
        if (query.StartsWith("mutation"))
        {
            var value = variables["input"]!.AsObject();
            Mutations.Add(value.DeepClone().AsObject());
            if (query.Contains("createIssue("))
            {
                if (RejectCreateOnce)
                {
                    RejectCreateOnce = false;
                    return Response(new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["type"] = "FORBIDDEN", ["message"] = "No issue created" }) });
                }
                CreateCount++;
                var item = Add("CREATED-" + CreateCount, title: value["title"]!.GetValue<string>());
                var content = Content(item["id"]!.GetValue<string>());
                content["body"] = value["body"]!.DeepClone();
                ApplyLabels(content, value);
                Items.Remove(item["id"]!.GetValue<string>());
                if (LoseCreateResponse) { LoseCreateResponse = false; throw new HttpRequestException("Lost create response"); }
                data = Mutation("createIssue", "issue", content["id"]!.GetValue<string>());
            }
            else if (query.Contains("addProjectV2ItemById("))
            {
                var contentId = value["contentId"]!.GetValue<string>();
                var item = Items.Values.SingleOrDefault(i => i["contentId"]?.GetValue<string>() == contentId);
                if (item is null)
                {
                    var itemId = "ADDED-" + contentId;
                    item = new JsonObject { ["id"] = itemId, ["isArchived"] = false, ["contentId"] = contentId, ["values"] = new JsonArray() };
                    Items.Add(itemId, item);
                }
                if (LoseAddResponse) { LoseAddResponse = false; throw new HttpRequestException("Lost add response"); }
                data = Mutation("addProjectV2ItemById", "item", item["id"]!.GetValue<string>());
            }
            else if (query.Contains("createLabel("))
            {
                var labelId = "LABEL-" + Labels.Count;
                Labels.Add(labelId, value["name"]!.GetValue<string>());
                data = Mutation("createLabel", "label", labelId);
            }
            else if (query.Contains("updateProjectV2ItemFieldValue(") || query.Contains("clearProjectV2ItemFieldValue("))
            {
                if (RejectFieldOnce)
                {
                    RejectFieldOnce = false;
                    return Response(new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["type"] = "FORBIDDEN", ["message"] = "secret-should-not-appear" }) });
                }
                var itemId = value["itemId"]!.GetValue<string>();
                var fieldId = value["fieldId"]!.GetValue<string>();
                var values = Items[itemId]["values"]!.AsArray();
                foreach (var old in values.Where(n => n?["field"]?["id"]?.GetValue<string>() == fieldId).ToList()) values.Remove(old);
                if (value["value"] is JsonObject changed)
                    values.Add(Value(fieldId, (changed["date"] ?? changed["singleSelectOptionId"])!.GetValue<string>(), changed["date"] is not null));
                var mutation = query.Contains("clearProject") ? "clearProjectV2ItemFieldValue" : "updateProjectV2ItemFieldValue";
                data = Mutation(mutation, "projectV2Item", itemId);
            }
            else
            {
                var contentId = (value["id"] ?? value["pullRequestId"] ?? value["draftIssueId"])!.GetValue<string>();
                var content = Contents[contentId];
                content["title"] = value["title"]!.DeepClone();
                content["body"] = value["body"]!.DeepClone();
                ApplyLabels(content, value);
                var type = content["__typename"]!.GetValue<string>();
                var (mutation, result) = type switch
                {
                    "Issue" => ("updateIssue", "issue"),
                    "PullRequest" => ("updatePullRequest", "pullRequest"),
                    _ => ("updateProjectV2DraftIssue", "draftIssue")
                };
                data = Mutation(mutation, result, contentId);
            }
        }
        else if (query.Contains("owner:organization(") || query.Contains("owner:user("))
            data = new JsonObject
            {
                ["owner"] = new JsonObject
                {
                    ["projectV2"] = new JsonObject
                    {
                        ["id"] = "PROJECT",
                        ["title"] = "Engineering",
                        ["url"] = (request.RequestUri!.Host == "api.github.com" ? "https://github.com" : request.RequestUri.GetLeftPart(UriPartial.Authority)) + "/orgs/acme/projects/1",
                        ["closed"] = false,
                        ["fields"] = Page(Fields(), null, 100)
                    }
                }
            };
        else if (query.Contains("fields(first:"))
            data = new JsonObject { ["node"] = new JsonObject { ["fields"] = Page(Fields(), after, 100) } };
        else if (query.Contains("repository(owner:") && query.Contains("issue(number:"))
        {
            var issue = Contents.Values.Single(x => x["number"]!.GetValue<int>() == variables["number"]!.GetValue<int>());
            data = new JsonObject { ["repository"] = new JsonObject { ["issue"] = new JsonObject { ["id"] = issue["id"]!.DeepClone() } } };
        }
        else if (query.Contains("repository(owner:"))
            data = new JsonObject { ["repository"] = new JsonObject { ["id"] = "REPO", ["isArchived"] = false, ["hasIssuesEnabled"] = true } };
        else if (query.Contains("nodes(ids:"))
            data = new JsonObject { ["nodes"] = new JsonArray(variables["ids"]!.AsArray().Select(id =>
                Users.TryGetValue(id!.GetValue<string>(), out var profile) ? profile.DeepClone() : null).ToArray()) };
        else if (query.Contains("user(login:"))
            data = new JsonObject { ["user"] = new JsonObject { ["id"] = "USER-1", ["login"] = variables["login"]!.DeepClone() } };
        else if (query.Contains("items(first:"))
            data = new JsonObject { ["node"] = new JsonObject { ["items"] = Page(Items.Values.Select(Render), after, PageSize) } };
        else if (query.Contains("fieldValues(first:100,after"))
            data = new JsonObject { ["node"] = new JsonObject { ["fieldValues"] = Page(Items[id!]["values"]!.AsArray(), after, 100) } };
        else if (query.Contains("... on ProjectV2Item"))
            data = new JsonObject { ["node"] = Items.TryGetValue(id!, out var item) ? Render(item) : null };
        else if (query.Contains("... on Repository"))
            data = new JsonObject { ["node"] = new JsonObject { ["labels"] = Page(Labels.Select(l => new JsonObject { ["id"] = l.Key, ["name"] = l.Value }), after, 100) } };
        else
        {
            var connection = query.Contains("labels(first:") ? "labels" : "assignees";
            data = new JsonObject { ["node"] = new JsonObject { [connection] = Page(Contents[id!][connection]!.AsArray(), after, 100) } };
        }
        return Response(new JsonObject { ["data"] = data });
    }

    private void ApplyLabels(JsonObject content, JsonObject input)
    {
        if (input["labelIds"] is JsonArray ids)
            content["labels"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject { ["id"] = id!.DeepClone(), ["name"] = Labels[id.GetValue<string>()] }).ToArray());
    }
    private static JsonObject Mutation(string operation, string field, string id) =>
        new() { [operation] = new JsonObject { [field] = new JsonObject { ["id"] = id } } };
    private static HttpResponseMessage Response(JsonObject data) => new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
}
