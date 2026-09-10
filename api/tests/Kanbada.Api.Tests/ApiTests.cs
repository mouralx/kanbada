using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;
using Kanbada.Api;
using Microsoft.EntityFrameworkCore;

public sealed class ApiFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory = null!;
    public string Connection = "";
    string schema = "test_" + Guid.NewGuid().ToString("N");
    public async Task InitializeAsync()
    {
        var basic = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        if (string.IsNullOrWhiteSpace(basic))
        {
            var root = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(root, "appsettings.Local.json")) && !File.Exists(Path.Combine(root, "api", "appsettings.Local.json")))
                root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run setup-local.mjs or set ConnectionStrings__Postgres before testing.");
            var path = File.Exists(Path.Combine(root, "appsettings.Local.json")) ? Path.Combine(root, "appsettings.Local.json") : Path.Combine(root, "api", "appsettings.Local.json");
            var config = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            basic = config["ConnectionStrings"]!["Postgres"]!.GetValue<string>();
        }

        await using (var c = new NpgsqlConnection(basic))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE SCHEMA {schema}", c);
            await cmd.ExecuteNonQueryAsync();
        }

        Connection = new NpgsqlConnectionStringBuilder(basic)
        {
            SearchPath = schema
        }.ToString();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton(NpgsqlDataSource.Create(Connection));
        }));
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await using var c = new NpgsqlConnection(Connection);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public HttpClient Client()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Kanbada-Request", "1");
        return client;
    }
}

public sealed class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    static async Task<JsonObject> Body(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    async Task<(HttpClient Client, string Email)> User()
    {
        var c = fixture.Client();
        var email = Guid.NewGuid() + "@example.test";
        await Body(await c.PostAsJsonAsync("/api/auth/register", new { email, password = "Correct-Horse-Test-Password", name = "Test " + Guid.NewGuid().ToString("N")[..6] }));
        // These workspace/legacy-login tests model accounts predating mandatory enrollment.
        // New-account enforcement is exercised separately in TwoFactorTests.
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KanbadaDbContext>().Users.Where(x => x.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorRequired, false));
        return (c, email);
    }

    static async Task<JsonObject> Save(HttpClient c, JsonObject state)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/workspaces/" + state["workspace"]!["id"]!.GetValue<string>());
        request.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
        request.Content = JsonContent.Create(state);
        return await Body(await c.SendAsync(request));
    }

    static JsonObject Card(string id = "KB-ABCDEF12") => new()
    {
        ["id"] = id,
        ["project"] = "my-activities",
        ["title"] = "An API card",
        ["description"] = "Context",
        ["status"] = "Backlog",
        ["priority"] = "High",
        ["due"] = "",
        ["labels"] = new JsonArray(),
        ["assignees"] = new JsonArray(),
        ["comments"] = new JsonArray(),
        ["checklist"] = new JsonArray(),
        ["attachments"] = new JsonArray(),
        ["history"] = new JsonArray()
    };
    [Fact]
    public async Task Authentication_UsesCookies_ProblemDetails_AndRevocableSessions()
    {
        using var anonymous = fixture.Client();
        var unauth = await anonymous.GetAsync("/api/workspaces");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);
        Assert.Equal("application/problem+json", unauth.Content.Headers.ContentType?.MediaType);
        var (c, email) = await User();
        using (c)
        {
            var session = await Body(await c.GetAsync("/api/auth/session"));
            Assert.Equal(email, session["user"]!["email"]!.ToString());
            var duplicate = await c.PostAsJsonAsync("/api/auth/register", new { email, password = "Correct-Horse-Test-Password", name = "Duplicate" });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            await c.PostAsync("/api/auth/logout", null);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/workspaces")).StatusCode);
            var wrong = await c.PostAsJsonAsync("/api/auth/login", new { email, password = "incorrect-password", name = "" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            var good = await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Correct-Horse-Test-Password", name = "" });
            Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
            Assert.True((await c.GetAsync("/api/workspaces")).IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task WorkspaceTransactions_ProtectInvariants_AndRejectStaleWrites()
    {
        var (c, _) = await User();
        using (c)
        {
            var state = await Body(await c.GetAsync("/api/workspaces/studio"));
            Assert.Equal("My Workspace", state["workspace"]!["name"]!.ToString());
            Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync("/api/workspaces/studio")).StatusCode);
            var malformed = state.DeepClone().AsObject();
            malformed["projects"]![0]!["archived"] = "not-a-boolean";
            using var malformedRequest = new HttpRequestMessage(HttpMethod.Put, "/api/workspaces/studio")
            {
                Content = JsonContent.Create(malformed)
            };
            malformedRequest.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
            Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(malformedRequest)).StatusCode);
            var invalid = state.DeepClone().AsObject();
            invalid["projects"] = new JsonArray();
            using var bad = new HttpRequestMessage(HttpMethod.Put, "/api/workspaces/studio")
            {
                Content = JsonContent.Create(invalid)
            };
            bad.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
            Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(bad)).StatusCode);
            state["tasks"]!.AsArray().Add(Card());
            var saved = await Save(c, state);
            Assert.Equal(2, saved["version"]!.GetValue<int>());
            Assert.Single(saved["tasks"]![0]!["history"]!.AsArray());
            Assert.NotEmpty(saved["tasks"]![0]!["history"]![0]!["actor"]!.ToString());
            using var stale = new HttpRequestMessage(HttpMethod.Put, "/api/workspaces/studio")
            {
                Content = JsonContent.Create(state)
            };
            stale.Headers.TryAddWithoutValidation("If-Match", "1");
            Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(stale)).StatusCode);
            var metrics = await Body(await c.GetAsync("/api/workspaces/studio/metrics"));
            Assert.Equal(0, metrics["overdue"]!.GetValue<int>());
            Assert.Equal(1, metrics["open"]!.GetValue<int>());
            saved["tasks"]![0]!["due"] = "2000-01-01";
            saved = await Save(c, saved);
            metrics = await Body(await c.GetAsync("/api/workspaces/studio/metrics"));
            Assert.Equal(1, metrics["overdue"]!.GetValue<int>());
            saved["tasks"]![0]!["due"] = "";
            await Save(c, saved);
            Assert.Equal(0, (await Body(await c.GetAsync("/api/workspaces/studio/metrics")))["overdue"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task MalformedCardCollections_ReturnBadRequest_WithoutPersistingChanges()
    {
        var (client, _) = await User();
        using (client)
        {
            var state = await Body(await client.GetAsync("/api/workspaces/studio"));
            foreach (var collection in new[] { "labels", "assignees", "comments", "checklist", "attachments" })
            {
                var invalid = state.DeepClone().AsObject();
                var card = Card();
                card[collection] = "not-an-array";
                invalid["tasks"]!.AsArray().Add(card);
                using var request = new HttpRequestMessage(HttpMethod.Put, "/api/workspaces/studio") { Content = JsonContent.Create(invalid) };
                request.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
                Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
            }
            var unchanged = await Body(await client.GetAsync("/api/workspaces/studio"));
            Assert.Empty(unchanged["tasks"]!.AsArray());
            Assert.Equal(state["version"]!.ToString(), unchanged["version"]!.ToString());
        }
    }

    [Fact]
    public async Task CreateCard_AcceptsOnlyTitle_GeneratesIdentityAndHistory_AndValidatesReferences()
    {
        var (client, _) = await User();
        using (client)
        {
            var response = await client.PostAsJsonAsync("/api/workspaces/studio/cards", new { title = "Created from Scalar" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var card = await Body(response);
            Assert.Matches("^KB-[A-F0-9]{32}$", card["id"]!.ToString());
            Assert.Equal("my-activities", card["project"]!.ToString());
            Assert.Equal("Backlog", card["status"]!.ToString());
            Assert.Equal("Medium", card["priority"]!.ToString());
            Assert.Equal("", card["due"]!.ToString());
            Assert.Single(card["history"]!.AsArray());
            Assert.Equal("\"2\"", response.Headers.ETag!.Tag);
            Assert.Equal(card["id"]!.ToString(), (await Body(await client.GetAsync(response.Headers.Location)))["id"]!.ToString());
            var invalid = await client.PostAsJsonAsync("/api/workspaces/studio/cards", new { title = "Bad reference", status = "Unknown status" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var state = await Body(await client.GetAsync("/api/workspaces/studio"));
            Assert.Single(state["tasks"]!.AsArray());
            Assert.Equal("2", state["version"]!.ToString());
        }
    }

    [Fact]
    public async Task Membership_RequiresInvitation_AndRemovalRevokesAccess()
    {
        var (owner, _) = await User();
        var (member, email) = await User();
        using (owner)
        using (member)
        {
            var state = await Body(await owner.PostAsJsonAsync("/api/workspaces", new { name = "Collaboration" }));
            var id = state["workspace"]!["id"]!.ToString();
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/workspaces/" + id)).StatusCode);
            state["members"]!.AsArray().Add(new JsonObject { ["name"] = "Invited colleague", ["email"] = email, ["initials"] = "IC", ["color"] = "#aabbcc" });
            state = await Save(owner, state);
            var token = state["members"]![1]!["invitationToken"]!.ToString();
            await Body(await member.PostAsync("/api/invitations/" + token + "/accept", null));
            var joined = await Body(await member.GetAsync("/api/workspaces/" + id));
            Assert.False(joined["workspace"]!["canManage"]!.GetValue<bool>());
            Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync("/api/workspaces/" + id)).StatusCode);
            var ownerName = joined["members"]![0]!["name"]!.ToString();
            joined["members"]![1]!["name"] = "Updated colleague";
            joined = await Save(member, joined);
            Assert.Equal(ownerName, joined["members"]![0]!["name"]!.ToString());
            Assert.Equal("Updated colleague", (await Body(await member.GetAsync("/api/auth/session")))["user"]!["name"]!.ToString());
            joined["tasks"]!.AsArray().Add(Card());
            await Save(member, joined);
            state = await Body(await owner.GetAsync("/api/workspaces/" + id));
            state["members"]!.AsArray().RemoveAt(1);
            await Save(owner, state);
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/workspaces/" + id)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/workspaces/" + id)).StatusCode);
        }
    }

    [Fact]
    public async Task FilesAndSharing_EnforceReadOnlyAccess_ExpiryAndRevocation()
    {
        var (owner, _) = await User();
        var (viewer, _) = await User();
        using (owner)
        using (viewer)
        {
            var bytes = new byte[]
            {
                0,
                1,
                2,
                255,
                8,
                9
            };
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(bytes), "file", "evidence.bin");
            var file = await Body(await owner.PostAsync("/api/workspaces/studio/files", form));
            var state = await Body(await owner.GetAsync("/api/workspaces/studio"));
            var card = Card();
            card["attachments"]!.AsArray().Add(file.DeepClone());
            state["tasks"]!.AsArray().Add(card);
            state = await Save(owner, state);
            Assert.Equal(bytes, await owner.GetByteArrayAsync("/api/files/" + file["id"]));
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/files/" + file["id"])).StatusCode);
            var restricted = await Body(await owner.PostAsJsonAsync("/api/workspaces/studio/cards/KB-ABCDEF12/share", new { access = "members", days = 7 }));
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/shares/" + restricted["token"])).StatusCode);
            var share = await Body(await owner.PostAsJsonAsync("/api/workspaces/studio/cards/KB-ABCDEF12/share", new { access = "signed-in", days = 7 }));
            var token = share["token"]!.ToString();
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/shares/" + restricted["token"])).StatusCode);
            var read = await Body(await viewer.GetAsync("/api/shares/" + token));
            Assert.Equal("An API card", read["task"]!["title"]!.ToString());
            Assert.Null(read["data"]!["tasks"]);
            Assert.Equal(bytes, await viewer.GetByteArrayAsync("/api/shares/" + token + "/files/" + file["id"]));
            await owner.DeleteAsync("/api/shares/" + token);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/shares/" + token)).StatusCode);
            state["tasks"] = new JsonArray();
            await Save(owner, state);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/files/" + file["id"])).StatusCode);
        }
    }

    [Fact]
    public async Task WritesRejectCrossSiteRequests_AndOpenApiIsAvailable()
    {
        using var client = fixture.Client();
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        var denied = await client.PostAsJsonAsync("/api/auth/register", new { email = "x@example.test", password = "long-enough-test-password", name = "X" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        var doc = await Body(await client.GetAsync("/api/openapi.json"));
        var update = doc["paths"]!["/api/workspaces/{id}"]!["put"]!;
        Assert.Contains(update["parameters"]!.AsArray(), parameter => parameter!["name"]!.ToString() == "If-Match");
        Assert.Contains(update["parameters"]!.AsArray(), parameter => parameter!["name"]!.ToString() == "X-Kanbada-Request");
        var redirect = await client.GetAsync("/api/scalar");
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        var scalar = await client.GetAsync(new Uri(redirect.RequestMessage!.RequestUri!, redirect.Headers.Location!));
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        var html = await scalar.Content.ReadAsStringAsync();
        Assert.Contains("Kanbada API", html);
        Assert.Contains("api/openapi.json", html);
    }
    [Fact]
    public async Task RelationalReferencesSurviveRenamesAndCascadeOnWorkspaceDeletion()
    {
        var (client, _) = await User();
        using (client)
        {
            var state = await Body(await client.PostAsJsonAsync("/api/workspaces", new { name = "Relational test" }));
            var workspace = state["workspace"]!["id"]!.ToString();
            foreach (var key in new[] { "buckets", "labels", "swimlanes" })
            {
                var definition = new JsonObject { ["id"] = key + "-stable", ["name"] = key, ["color"] = "#123456", ["complete"] = false };
                if (key == "swimlanes") definition["project"] = "my-activities";
                state[key]!.AsArray().Add(definition);
            }
            var card = Card();
            card["bucket"] = "buckets";
            card["swimlane"] = "swimlanes";
            card["labels"] = new JsonArray("labels");
            card["assignees"] = new JsonArray(state["members"]![0]!["name"]!.ToString());
            state["tasks"]!.AsArray().Add(card);
            state = await Save(client, state);
            foreach (var key in new[] { "buckets", "labels", "swimlanes" }) state[key]![0]!["name"] = key + " renamed";
            state["tasks"]![0]!["bucket"] = "buckets renamed";
            state["tasks"]![0]!["swimlane"] = "swimlanes renamed";
            state["tasks"]![0]!["labels"] = new JsonArray("labels renamed");
            state = await Save(client, state);
            Assert.Equal("buckets renamed", state["tasks"]![0]!["bucket"]!.ToString());
            var metrics = await Body(await client.GetAsync($"/api/workspaces/{workspace}/metrics?bucket=buckets%20renamed&swimlane=swimlanes%20renamed"));
            Assert.Equal(1, metrics["total"]!.GetValue<int>());
            Assert.Equal(0, metrics["unassigned"]!.GetValue<int>());
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/workspaces/" + workspace)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/workspaces/" + workspace)).StatusCode);
        }
    }

    [Fact]
    public async Task ConcurrentWorkspaceUpdatesAllowOneWriterAndPreserveTheWinner()
    {
        var (client, _) = await User();
        using (client)
        {
            var state = await Body(await client.GetAsync("/api/workspaces/studio"));
            HttpRequestMessage Update(string title)
            {
                var copy = state.DeepClone().AsObject();
                var card = Card();
                card["title"] = title;
                copy["tasks"]!.AsArray().Add(card);
                var request = new HttpRequestMessage(HttpMethod.Put, "/api/workspaces/studio") { Content = JsonContent.Create(copy) };
                request.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
                return request;
            }
            using var first = Update("First writer");
            using var second = Update("Second writer");
            var results = await Task.WhenAll(client.SendAsync(first), client.SendAsync(second));
            Assert.Single(results, x => x.IsSuccessStatusCode);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var winner = await Body(results.Single(x => x.IsSuccessStatusCode));
            var current = await Body(await client.GetAsync("/api/workspaces/studio"));
            Assert.True(JsonNode.DeepEquals(winner, current));
        }
    }

    [Fact]
    public async Task PartialEditsReturnSmallCanonicalDeltasAndPreserveUnchangedContent()
    {
        var (client, _) = await User();
        using (client)
        {
            var state = await Body(await client.GetAsync("/api/workspaces/studio"));
            state["workspace"]!["banner"] = new string('x', 250000);
            var card = Card();
            card["description"] = new string('d', 100000);
            state["tasks"]!.AsArray().Add(card);
            state = await Save(client, state);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/workspaces/studio")
            {
                Content = JsonContent.Create(new { changes = new[] { new { op = "replace", path = "/tasks/0/title", value = "A small edit" } } })
            };
            request.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
            Assert.True((await request.Content.ReadAsByteArrayAsync()).Length < 150);
            var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, text);
            Assert.True(text.Length < 2000, $"Delta response was {text.Length} characters");
            Assert.DoesNotContain("banner", text);
            Assert.DoesNotContain("description", text);
            var delta = System.Text.Json.JsonSerializer.Deserialize<Kanbada.Api.ChangeResult>(text, System.Text.Json.JsonSerializerOptions.Web)!;
            var reconstructed = Kanbada.Api.StateChanges.Apply(state, delta.Changes.Where(x => x.Path != "/version" && !x.Path.Contains("/history")).ToArray());
            var current = await Body(await client.GetAsync("/api/workspaces/studio"));
            Assert.Equal("A small edit", reconstructed["tasks"]![0]!["title"]!.ToString());
            Assert.Equal(state["workspace"]!["banner"]!.ToString(), current["workspace"]!["banner"]!.ToString());
            Assert.Equal(state["tasks"]![0]!["description"]!.ToString(), current["tasks"]![0]!["description"]!.ToString());
            Assert.Equal(2, current["tasks"]![0]!["history"]!.AsArray().Count);
            Assert.Equal(current["version"]!.ToString(), response.Headers.ETag!.Tag.Trim('"'));
            using var unchanged = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces/studio");
            unchanged.Headers.IfNoneMatch.Add(response.Headers.ETag);
            var cached = await client.SendAsync(unchanged);
            Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
            Assert.Empty(await cached.Content.ReadAsByteArrayAsync());
            using var anonymous = fixture.Client();
            using var denied = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces/studio");
            denied.Headers.IfNoneMatch.Add(response.Headers.ETag);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(denied)).StatusCode);
        }
    }

    [Fact]
    public async Task PartialEditsRejectStaleMalformedAndUnauthorizedChangesAtomically()
    {
        var (client, _) = await User();
        using (client)
        {
            var state = await Body(await client.GetAsync("/api/workspaces/studio"));
            foreach (var path in new[] { "/workspace/ownerId", "/tasks/999/title", "/__proto__/polluted", "/members/0/userId" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/workspaces/studio")
                {
                    Content = JsonContent.Create(new { changes = new[] { new { op = "add", path = "/activity/0", value = "Must roll back" }, new { op = "replace", path, value = "invalid" } } })
                };
                request.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
                Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
                Assert.True(JsonNode.DeepEquals(state, await Body(await client.GetAsync("/api/workspaces/studio"))));
            }
            var body = new { changes = new[] { new { op = "add", path = "/activity/0", value = "Changed" } } };
            Assert.Equal((HttpStatusCode)428, (await client.PatchAsJsonAsync("/api/workspaces/studio", body)).StatusCode);
            using var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/workspaces/studio") { Content = JsonContent.Create(body) };
            stale.Headers.TryAddWithoutValidation("If-Match", "0");
            Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(stale)).StatusCode);
        }
    }

}
