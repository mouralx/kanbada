using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using OtpNet;

public sealed class GitHubUnitTests
{
    [Fact]
    public void DestinationPolicyAllowsOnlyExplicitHttpsServers()
    {
        var policy = new GitHubDestinationPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GitHub:AllowedHosts"] = "git.example.test:8443" }).Build());
        Assert.Equal("https://api.github.com/graphql", policy.Api("https://github.com").AbsoluteUri);
        Assert.Equal("https://git.example.test:8443/api/graphql", policy.Api("https://git.example.test:8443").AbsoluteUri);
        foreach (var url in new[] { "http://github.com", "https://api.github.com", "https://github.com.evil.test", "https://github.com:8443", "https://127.0.0.1", "https://github.com/api/graphql", "https://github.com/?x=1", "https://user:pass@github.com" })
            Assert.Throws<ApiError>(() => policy.Validate(url));
        Assert.Throws<GitHubSyncException>(() => GitHubDestinationPolicy.BrowserUrl("https://github.com", "javascript:alert(1)"));
        Assert.Throws<GitHubSyncException>(() => GitHubDestinationPolicy.BrowserUrl("https://github.com", "https://evil.test/issues/1"));
    }

    [Theory]
    [InlineData("github-to-kanbada", "kanbada", "github")]
    [InlineData("kanbada-to-github", "github", "kanbada")]
    [InlineData("bidirectional", "github", "github")]
    [InlineData("bidirectional", "kanbada", "kanbada")]
    public void ConflictWinnerPreservesDirectionAndCreationOrigin(string direction, string origin, string winner) =>
        Assert.Equal(winner, CardSynchronization.Winner("github", direction, origin, "new-local", "new-remote", "old", "old"));
}

public sealed class GitHubTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private sealed record SetupData(IServiceScope Scope, KanbadaDbContext Db, GitHubConnectionEntity Connection, FakeGitHub Remote,
        GitHubClient Client, GitHubSyncEngine Engine, GitHubSettings Settings, GitHubConnectionInput Input);

    private async Task<SetupData> Setup(string direction = "bidirectional", bool manyFields = false)
    {
        using var start = fixture.Client();
        var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        var owner = new UserEntity { Id = Guid.NewGuid(), Email = Guid.NewGuid() + "@example.test", Name = "GitHub owner" };
        db.Add(owner);
        await db.SaveChangesAsync();
        var workspace = await scope.ServiceProvider.GetRequiredService<WorkspaceStore>().Create(owner.Id, "GitHub " + Guid.NewGuid());
        var remote = new FakeGitHub { ManyFields = manyFields };
        var secrets = scope.ServiceProvider.GetRequiredService<GitHubSecrets>();
        var policy = scope.ServiceProvider.GetRequiredService<GitHubDestinationPolicy>();
        var client = new GitHubClient(new HttpClient(remote), secrets, policy);
        var settings = new GitHubSettings(db, scope.ServiceProvider.GetRequiredService<WorkspaceResolver>(), secrets, policy, client);
        var input = new GitHubConnectionInput(0, "https://github.com", "acme", "organization", 1, "acme/repo", "test-github-token",
            direction, "STATUS", "PRIORITY", "DUE", true, true, "* * * * *", "UTC", true,
            [new("status", "backlog", "todo"), new("status", "backlog", GitHubClient.Unset, false), new("status", "progress", "doing"), new("status", "done", "done"),
             new("priority", "Low", "low"), new("priority", "Medium", "medium"), new("priority", "High", "high"),
             new("priority", "Medium", GitHubClient.Unset, false), new("assignee", owner.Id.ToString(), "USER-1")]);
        await settings.Save(workspace, "my-activities", input, default);
        var c = (await settings.Find(workspace, "my-activities", default))!;
        var engine = new GitHubSyncEngine(db, scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(), client, NullLogger<GitHubSyncEngine>.Instance);
        return new(scope, db, c, remote, client, engine, settings, input);
    }

    private static async Task Run(SetupData s)
    {
        s.Db.ChangeTracker.Clear();
        await s.Db.Set<GitHubConnectionEntity>().Where(c => c.Id == s.Connection.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await s.Engine.Run(s.Connection.Id, default);
        s.Db.ChangeTracker.Clear();
    }

    private static async Task<CardEntity> AddCard(SetupData s, string id = "KB-LOCAL")
    {
        var card = new CardEntity { WorkspaceId = s.Connection.WorkspaceId, Id = id, ProjectId = s.Connection.ProjectId, Title = "Local title", StatusId = "backlog", Priority = "Medium", Due = new DateOnly(2026, 10, 10) };
        s.Db.Add(card);
        await s.Db.SaveChangesAsync();
        return card;
    }

    private static async Task<HttpClient> AuthenticatedClient(WebApplicationFactory<Program> factory, Guid user)
    {
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await scope.ServiceProvider.GetRequiredService<Auth>().Session(context, user);
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        var sid = await db.Sessions.Where(s => s.UserId == user).Select(s => s.Id).SingleAsync();
        var factor = scope.ServiceProvider.GetRequiredService<TwoFactor>();
        var setup = await factor.Setup(user, "");
        await factor.Confirm(user, new TwoFactorInput("", new Totp(Base32Encoding.ToBytes(setup.Secret)).ComputeTotp()), sid);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kanbada-Request", "1");
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", context.Response.Headers.SetCookie.Select(c => c!.Split(';')[0])));
        return client;
    }

    [Fact]
    public async Task ApiAuthorizesOwnersAndMembersAndRecoversUncertainCreations()
    {
        var s = await Setup("kanbada-to-github");
        using (s.Scope)
        {
            var card = await AddCard(s);
            s.Remote.LoseCreateResponse = true;
            await Run(s);
            var workspace = s.Connection.WorkspaceId;
            var owner = await s.Db.Workspaces.Where(w => w.Id == workspace).Select(w => w.OwnerId).SingleAsync();
            var email = await s.Db.Users.Where(u => u.Id == owner).Select(u => u.Email).SingleAsync();
            using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:AdminEmails"] = email }));
                builder.ConfigureTestServices(services => services.AddScoped(sp => new GitHubClient(new HttpClient(s.Remote),
                    sp.GetRequiredService<GitHubSecrets>(), sp.GetRequiredService<GitHubDestinationPolicy>())));
            });
            using var client = await AuthenticatedClient(factory, owner);
            var path = $"/api/workspaces/{workspace}/projects/my-activities/github";
            var read = await ApiTests.Body(await client.GetAsync(path));
            Assert.DoesNotContain("test-github-token", read.ToJsonString());
            Assert.DoesNotContain("protectedToken", read.ToJsonString());
            var linkId = read["connection"]!["problems"]![0]!["id"]!.GetValue<string>();
            using (var wrong = await client.PostAsJsonAsync(path + "/links/" + linkId + "/resolve", new { issueUrl = "https://github.com/wrong/repo/issues/1" }))
                Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            using (var resolved = await client.PostAsJsonAsync(path + "/links/" + linkId + "/resolve", new { issueUrl = "https://github.com/acme/repo/issues/1" }))
                Assert.Equal(HttpStatusCode.NoContent, resolved.StatusCode);
            using (var queued = await client.PostAsync(path + "/run", null)) Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            s.Db.ChangeTracker.Clear();
            await s.Engine.Run(s.Connection.Id, default);
            var links = await ApiTests.Body(await client.GetAsync($"/api/workspaces/{workspace}/cards/{card.Id}/github"));
            Assert.Single(links["links"]!.AsArray());
            Assert.Equal(1, s.Remote.CreateCount);
            using (var approved = await client.PostAsJsonAsync("/api/github/hosts", new { baseUrl = "https://github.example.test" }))
                Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var enterprise = s.Input with { BaseUrl = "https://github.example.test" };
            await ApiTests.Body(await client.PostAsJsonAsync(path + "/test", enterprise));
            Assert.Contains(s.Remote.Destinations, uri => uri.AbsoluteUri == "https://github.example.test/api/graphql");
            using var revoke = new HttpRequestMessage(HttpMethod.Delete, "/api/github/hosts") { Content = JsonContent.Create(new { baseUrl = enterprise.BaseUrl }) };
            using (var removed = await client.SendAsync(revoke)) Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
            var requests = s.Remote.Queries.Count;
            using (var denied = await client.PostAsJsonAsync(path + "/test", enterprise)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Equal(requests, s.Remote.Queries.Count);
            var other = new UserEntity { Id = Guid.NewGuid(), Name = "Other", Email = Guid.NewGuid() + "@example.test" };
            s.Db.Add(other);
            await s.Db.SaveChangesAsync();
            using var member = await AuthenticatedClient(factory, other.Id);
            using (var denied = await member.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await member.GetAsync($"/api/workspaces/{workspace}/cards/{card.Id}/github")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            s.Db.Add(new MemberEntity { WorkspaceId = workspace, UserId = other.Id, Email = other.Email, Name = other.Name, Color = "#123456" });
            await s.Db.SaveChangesAsync();
            await ApiTests.Body(await member.GetAsync($"/api/workspaces/{workspace}/cards/{card.Id}/github"));
            using (var denied = await member.PostAsJsonAsync("/api/github/hosts", new { baseUrl = enterprise.BaseUrl })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            await AddCard(s, "KB-RETRY");
            s.Remote.RejectCreateOnce = true;
            await Run(s);
            var pending = await s.Db.Set<GitHubLinkEntity>().SingleAsync(l => l.ConnectionId == s.Connection.Id && l.CreationPending);
            await using (var lease = new NpgsqlConnection(fixture.Connection))
            {
                await lease.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@id,0))", lease);
                command.Parameters.AddWithValue("id", "github:" + s.Connection.Id);
                await command.ExecuteNonQueryAsync();
                using var blocked = await client.PostAsJsonAsync(path + "/links/" + pending.Id + "/resolve", new { retryCreation = true });
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@id,0))";
                await command.ExecuteNonQueryAsync();
            }
            using (var retry = await client.PostAsJsonAsync(path + "/links/" + pending.Id + "/resolve", new { retryCreation = true }))
                Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
            await Run(s);
            Assert.Equal(2, s.Remote.CreateCount);
            Assert.Equal(2, s.Remote.Items.Count);
            s.Remote.RateLimited = true;
            var beforePause = s.Remote.Queries.Count;
            var paused = await ApiTests.Body(await client.PostAsJsonAsync(path + "/pause", new { version = 1 }));
            Assert.False(paused["enabled"]!.GetValue<bool>());
            Assert.Equal(beforePause, s.Remote.Queries.Count);
        }
    }

    [Fact]
    public async Task LocalEditsDuringOutboundRequestsArePreservedForTheNextRun()
    {
        var s = await Setup("kanbada-to-github");
        using (s.Scope)
        {
            var card = await AddCard(s);
            await Run(s);
            await s.Db.Cards.Where(c => c.WorkspaceId == card.WorkspaceId && c.Id == card.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.Title, "First edit"));
            s.Remote.BeforeRequest = async query =>
            {
                if (!query.Contains("updateIssue(")) return;
                s.Remote.BeforeRequest = null;
                using var scope = fixture.Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
                await db.Cards.Where(c => c.WorkspaceId == card.WorkspaceId && c.Id == card.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.Title, "Later edit"));
                await db.Workspaces.Where(w => w.Id == card.WorkspaceId).ExecuteUpdateAsync(x => x.SetProperty(w => w.Version, w => w.Version + 1));
            };
            await Run(s);
            Assert.Equal("Later edit", (await s.Db.Cards.SingleAsync(c => c.WorkspaceId == card.WorkspaceId && c.Id == card.Id)).Title);
            await Run(s);
            Assert.Equal("Later edit", s.Remote.Contents.Values.Single()["title"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task DisabledOptionalFieldsPreserveLocalValues()
    {
        var s = await Setup("github-to-kanbada");
        using (s.Scope)
        {
            await s.Settings.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, s.Input with
            {
                Version = 1,
                PriorityFieldId = "",
                DueFieldId = "",
                SyncLabels = false,
                Mappings = s.Input.Mappings.Where(m => m.Kind != "priority").ToList()
            }, default);
            s.Remote.Add("ONE");
            await Run(s);
            var card = await s.Db.Cards.SingleAsync(c => c.WorkspaceId == s.Connection.WorkspaceId);
            card.Priority = "High";
            card.Due = new DateOnly(2030, 1, 1);
            await s.Db.SaveChangesAsync();
            s.Remote.Content("ONE")["title"] = "Changed remotely";
            await Run(s);
            card = await s.Db.Cards.SingleAsync(c => c.WorkspaceId == s.Connection.WorkspaceId);
            Assert.Equal("Changed remotely", card.Title);
            Assert.Equal("High", card.Priority);
            Assert.Equal(new DateOnly(2030, 1, 1), card.Due);
        }
    }
    [Fact]
    public async Task PaginatesItemsFieldsLabelsAndAssigneesWithoutDuplicateImports()
    {
        var s = await Setup("github-to-kanbada", manyFields: true);
        using (s.Scope)
        {
            for (var i = 0; i < 55; i++) s.Remote.Add("ITEM-" + i, i % 3 == 0 ? "DraftIssue" : i % 3 == 1 ? "Issue" : "PullRequest");
            var content = s.Remote.Content("ITEM-1");
            for (var i = 0; i < 103; i++)
            {
                content["labels"]!.AsArray().Add(new JsonObject { ["id"] = "LABEL-" + i, ["name"] = "Label " + i });
                content["assignees"]!.AsArray().Add(new JsonObject { ["id"] = "USER-" + i });
            }
            var item = s.Remote.Items["ITEM-1"];
            var values = item["values"]!.AsArray();
            for (var i = 0; i < 101; i++) values.Insert(0, new JsonObject());
            await Run(s);
            Assert.Equal(55, await s.Db.Cards.CountAsync(c => c.WorkspaceId == s.Connection.WorkspaceId));
            Assert.Equal(55, await s.Db.Set<GitHubLinkEntity>().CountAsync(l => l.ConnectionId == s.Connection.Id));
            Assert.Equal(103, await s.Db.CardLabels.CountAsync(l => l.WorkspaceId == s.Connection.WorkspaceId));
            Assert.Single(await s.Db.CardAssignees.Where(l => l.WorkspaceId == s.Connection.WorkspaceId).ToListAsync());
            Assert.Single(await s.Db.Notifications.Where(n => n.WorkspaceId == s.Connection.WorkspaceId).ToListAsync());
            Assert.Contains(s.Remote.Queries, q => q.Contains("fields(first:100,after"));
            Assert.Contains(s.Remote.Queries, q => q.Contains("fieldValues(first:100,after"));
            Assert.Contains(s.Remote.Queries, q => q.Contains("labels(first:100,after"));
            Assert.Contains(s.Remote.Queries, q => q.Contains("assignees(first:100,after"));
            Assert.True(s.Remote.Queries.Count(q => q.Contains("items(first:50")) >= 28);
            var version = await s.Db.Workspaces.Where(w => w.Id == s.Connection.WorkspaceId).Select(w => w.Version).SingleAsync();
            await Run(s);
            Assert.Equal(version, await s.Db.Workspaces.Where(w => w.Id == s.Connection.WorkspaceId).Select(w => w.Version).SingleAsync());
            Assert.Equal(55, await s.Db.HistoryEntries.CountAsync(h => h.WorkspaceId == s.Connection.WorkspaceId));
            Assert.Empty(s.Remote.Mutations);
        }
    }

    [Fact]
    public async Task OutboundCreatesOneIssueAndClearsOptionalFieldsWithoutWritingAssigneesOrState()
    {
        var s = await Setup("kanbada-to-github");
        using (s.Scope)
        {
            var card = await AddCard(s);
            card.Due = null;
            s.Db.Add(new LabelEntity { WorkspaceId = card.WorkspaceId, Id = "label", Name = "Frontend", Color = "#123456" });
            s.Db.Add(new CardLabelEntity { WorkspaceId = card.WorkspaceId, CardId = card.Id, LabelId = "label" });
            await s.Db.SaveChangesAsync();
            await Run(s);
            Assert.Equal(1, s.Remote.CreateCount);
            Assert.Single(s.Remote.Items);
            var link = await s.Db.Set<GitHubLinkEntity>().SingleAsync(l => l.ConnectionId == s.Connection.Id);
            Assert.True(link.Initialized);
            Assert.Null(link.LastError);
            Assert.NotNull(link.Url);
            Assert.Contains(s.Remote.Queries, q => q.Contains("clearProjectV2ItemFieldValue("));
            Assert.Contains(s.Remote.Labels.Values, name => name == "Frontend");
            await Run(s);
            Assert.Equal(1, s.Remote.CreateCount);
            Assert.All(s.Remote.Mutations, input =>
            {
                Assert.False(input.ContainsKey("state"));
                Assert.False(input.ContainsKey("assigneeIds"));
            });
            Assert.DoesNotContain(s.Remote.Queries, q => q.Contains("mergePullRequest") || q.Contains("deleteIssue") || q.Contains("deleteProjectV2Item"));
        }
    }

    [Theory]
    [InlineData("Issue")]
    [InlineData("PullRequest")]
    [InlineData("DraftIssue")]
    public async Task BidirectionalUpdatesEachContentTypeAndPreservesDraftLabels(string type)
    {
        var s = await Setup();
        using (s.Scope)
        {
            s.Remote.Add("ONE", type);
            await Run(s);
            var card = await s.Db.Cards.SingleAsync(c => c.WorkspaceId == s.Connection.WorkspaceId);
            card.Title = "Edited locally";
            s.Db.Add(new LabelEntity { WorkspaceId = card.WorkspaceId, Id = "custom", Name = "Local label", Color = "#123456" });
            s.Db.Add(new CardLabelEntity { WorkspaceId = card.WorkspaceId, CardId = card.Id, LabelId = "custom" });
            await s.Db.SaveChangesAsync();
            await Run(s);
            Assert.Equal("Edited locally", s.Remote.Content("ONE")["title"]!.GetValue<string>());
            Assert.Single(await s.Db.CardLabels.Where(l => l.WorkspaceId == card.WorkspaceId).ToListAsync());
            if (type == "DraftIssue") Assert.All(s.Remote.Mutations, m => Assert.False(m.ContainsKey("labelIds")));
            s.Remote.Content("ONE")["title"] = "Edited remotely";
            await Run(s);
            Assert.Equal("Edited remotely", (await s.Db.Cards.SingleAsync(c => c.WorkspaceId == card.WorkspaceId)).Title);
            Assert.Single(await s.Db.CardLabels.Where(l => l.WorkspaceId == card.WorkspaceId).ToListAsync());
        }
    }

    [Fact]
    public async Task InterruptedProjectEnrollmentResumesWithoutCreatingAnotherIssue()
    {
        var s = await Setup("kanbada-to-github");
        using (s.Scope)
        {
            await AddCard(s);
            s.Remote.LoseAddResponse = true;
            await Run(s);
            var link = await s.Db.Set<GitHubLinkEntity>().SingleAsync(l => l.ConnectionId == s.Connection.Id);
            Assert.NotNull(link.ContentId);
            Assert.Null(link.ItemId);
            Assert.False(link.CreationPending);
            Assert.False(link.Initialized);
            Assert.Single(s.Remote.Items);
            await Run(s);
            Assert.Equal(1, s.Remote.CreateCount);
            Assert.Single(s.Remote.Items);
            Assert.True((await s.Db.Set<GitHubLinkEntity>().SingleAsync(l => l.Id == link.Id)).Initialized);
        }
    }

    [Fact]
    public async Task UncertainIssueCreationBlocksRetriesAndUnlinkedImports()
    {
        var s = await Setup();
        using (s.Scope)
        {
            await AddCard(s);
            s.Remote.LoseCreateResponse = true;
            await Run(s);
            var link = await s.Db.Set<GitHubLinkEntity>().SingleAsync(l => l.ConnectionId == s.Connection.Id);
            Assert.True(link.CreationPending);
            Assert.Null(link.ContentId);
            s.Remote.Add("UNRELATED");
            await Run(s);
            Assert.Equal(1, s.Remote.CreateCount);
            Assert.Single(await s.Db.Cards.Where(c => c.WorkspaceId == s.Connection.WorkspaceId).ToListAsync());
            var connection = await s.Db.Set<GitHubConnectionEntity>().SingleAsync(c => c.Id == s.Connection.Id);
            Assert.Contains("pending", connection.LastError);
        }
    }

    [Fact]
    public async Task PartialOutboundWritesResumeWithoutImportingPartiallyChangedRemoteState()
    {
        var s = await Setup();
        using (s.Scope)
        {
            s.Remote.Add("ONE");
            await Run(s);
            var card = await s.Db.Cards.SingleAsync(c => c.WorkspaceId == s.Connection.WorkspaceId);
            card.Title = "Local update";
            card.StatusId = "progress";
            await s.Db.SaveChangesAsync();
            s.Remote.RejectFieldOnce = true;
            await Run(s);
            var link = await s.Db.Set<GitHubLinkEntity>().SingleAsync(l => l.ConnectionId == s.Connection.Id);
            Assert.True(link.WritePending);
            Assert.DoesNotContain("secret-should-not-appear", link.LastError);
            Assert.Equal("Local update", s.Remote.Content("ONE")["title"]!.GetValue<string>());
            await Run(s);
            card = await s.Db.Cards.SingleAsync(c => c.Id == card.Id && c.WorkspaceId == card.WorkspaceId);
            Assert.Equal("progress", card.StatusId);
            Assert.Equal("doing", s.Remote.Items["ONE"]["values"]!.AsArray().Single(n => n!["field"]!["id"]!.GetValue<string>() == "STATUS")!["optionId"]!.GetValue<string>());
            Assert.False((await s.Db.Set<GitHubLinkEntity>().SingleAsync(l => l.Id == link.Id)).WritePending);
        }
    }

    [Fact]
    public async Task ArchivedDeletedAndRedactedItemsNeverDeleteOrRecreateCards()
    {
        var s = await Setup("github-to-kanbada");
        using (s.Scope)
        {
            s.Remote.Add("ARCHIVED");
            s.Remote.Add("DELETED");
            s.Remote.Add("REDACTED");
            await Run(s);
            s.Remote.Items["ARCHIVED"]["isArchived"] = true;
            s.Remote.Items.Remove("DELETED");
            s.Remote.Items["REDACTED"]["contentId"] = null;
            await Run(s);
            Assert.Equal(3, await s.Db.Cards.CountAsync(c => c.WorkspaceId == s.Connection.WorkspaceId));
            Assert.Equal(3, await s.Db.Set<GitHubLinkEntity>().CountAsync(l => l.ConnectionId == s.Connection.Id && l.LastError != null));
            Assert.Empty(s.Remote.Mutations);
            await s.Db.Set<GitHubConnectionEntity>().Where(c => c.Id == s.Connection.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.Enabled, false));
            Assert.Equal(3, await ExternalCardPolicy.ReadOnlyCardIds(s.Db, s.Connection.WorkspaceId).CountAsync());
        }
    }

    [Fact]
    public async Task InboundOnlyPoliciesRejectEditsAndSettingsDoNotExposeTokens()
    {
        var s = await Setup("github-to-kanbada");
        using (s.Scope)
        {
            s.Remote.Add("ONE");
            await Run(s);
            var owner = await s.Db.Workspaces.Where(w => w.Id == s.Connection.WorkspaceId).Select(w => w.OwnerId).SingleAsync();
            var store = s.Scope.ServiceProvider.GetRequiredService<WorkspaceStore>();
            var state = (await store.Read(s.Connection.WorkspaceId, owner)).State;
            Assert.True(state["tasks"]![0]!["readOnly"]!.GetValue<bool>());
            var changed = state.DeepClone().AsObject();
            changed["tasks"]![0]!["title"] = "Forbidden edit";
            Assert.Throws<ApiError>(() => ExternalCardPolicy.ValidateChanges(state, changed));
            var safe = JsonSerializer.Serialize(await s.Settings.Read(s.Connection.WorkspaceId, s.Connection.ProjectId, default));
            Assert.DoesNotContain("test-github-token", safe);
            Assert.DoesNotContain(s.Connection.ProtectedToken, safe);
            await Assert.ThrowsAsync<ApiError>(() => s.Settings.Authorize(s.Connection.WorkspaceId.ToString(), s.Connection.ProjectId, Guid.NewGuid(), default));
            var reused = await s.Settings.ForTest(s.Connection.WorkspaceId, s.Connection.ProjectId, s.Input with { Token = "" }, default);
            Assert.Equal("test-github-token", s.Scope.ServiceProvider.GetRequiredService<GitHubSecrets>().Unprotect(reused.ProtectedToken));
            await Assert.ThrowsAsync<ApiError>(() => s.Settings.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, s.Input, default));
        }
    }

    [Fact]
    public async Task RateLimitsAreVisibleAndRespectResetTime()
    {
        var s = await Setup();
        using (s.Scope)
        {
            s.Remote.RateLimited = true;
            await Run(s);
            var c = await s.Db.Set<GitHubConnectionEntity>().SingleAsync(c => c.Id == s.Connection.Id);
            Assert.Contains("rate limit", c.LastError);
            Assert.DoesNotContain("secret-should-not-appear", c.LastError);
            Assert.True(c.NextRunAt > DateTimeOffset.UtcNow.AddMinutes(50));
            Assert.Empty(s.Remote.Mutations);
        }
    }

    [Fact]
    public async Task WorkerLockAndMidRunPausePreventConcurrentOrFurtherWrites()
    {
        var s = await Setup();
        using (s.Scope)
        {
            await AddCard(s);
            await using var connection = new NpgsqlConnection(fixture.Connection);
            await connection.OpenAsync();
            await using var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@id,0))", connection);
            acquire.Parameters.AddWithValue("id", "github:" + s.Connection.Id);
            await acquire.ExecuteNonQueryAsync();
            await Run(s);
            Assert.Equal(0, s.Remote.CreateCount);
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@id,0))", connection);
            release.Parameters.AddWithValue("id", "github:" + s.Connection.Id);
            await release.ExecuteNonQueryAsync();
            s.Remote.BeforeRequest = async query =>
            {
                if (!query.Contains("items(first:")) return;
                using var separate = s.Scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
                await separate.ServiceProvider.GetRequiredService<KanbadaDbContext>().Set<GitHubConnectionEntity>().Where(c => c.Id == s.Connection.Id)
                    .ExecuteUpdateAsync(x => x.SetProperty(c => c.Enabled, false).SetProperty(c => c.Version, c => c.Version + 1));
            };
            await Run(s);
            Assert.Equal(0, s.Remote.CreateCount);
        }
    }

    [Fact]
    public async Task InterruptedManualRunsRemainDueAfterWorkerRestart()
    {
        var s = await Setup("kanbada-to-github");
        using (s.Scope)
        using (var cancellation = new CancellationTokenSource())
        {
            await AddCard(s);
            await s.Db.Set<GitHubConnectionEntity>().Where(c => c.Id == s.Connection.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.NextRunAt, DateTimeOffset.UtcNow.AddDays(1)).SetProperty(c => c.RequestedAt, DateTimeOffset.UtcNow));
            s.Remote.BeforeRequest = _ => { cancellation.Cancel(); return Task.CompletedTask; };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.Engine.Run(s.Connection.Id, cancellation.Token));
            s.Db.ChangeTracker.Clear();
            var stored = await s.Db.Set<GitHubConnectionEntity>().SingleAsync(c => c.Id == s.Connection.Id);
            Assert.Null(stored.RequestedAt);
            Assert.True(stored.NextRunAt <= DateTimeOffset.UtcNow);
            s.Remote.BeforeRequest = null;
            await s.Engine.Run(s.Connection.Id, default);
            Assert.Equal(1, s.Remote.CreateCount);
        }
    }

    [Fact]
    public async Task OutboundLabelsReuseCaseAndSpaceEquivalentRepositoryDefinitions()
    {
        var s = await Setup("kanbada-to-github");
        using (s.Scope)
        {
            s.Remote.Labels.Add("EXISTING", " frontend ");
            Assert.Equal(new[] { "EXISTING" }, await s.Client.LabelIds(s.Connection, "REPO", ["Frontend", "FRONTEND"], default));
            Assert.Empty(s.Remote.Mutations);
        }
    }

    [Fact]
    public async Task ConcurrentConnectorSavesChooseExactlyOneProvider()
    {
        var s = await Setup();
        using (s.Scope)
        using (var otherScope = fixture.Factory.Services.CreateScope())
        {
            await s.Db.Set<GitHubConnectionEntity>().Where(c => c.Id == s.Connection.Id).ExecuteDeleteAsync();
            s.Db.ChangeTracker.Clear();
            var jira = otherScope.ServiceProvider.GetRequiredService<JiraSettings>();
            var input = new JiraConnectionInput(0, "https://example.atlassian.net", "cloud", "test@example.test", "jira-token", "project = TEST",
                "TEST", "1", "jira-to-kanbada", "* * * * *", "UTC", true,
                [new("status", "backlog", "1"), new("priority", "Low", "1"), new("priority", "Medium", "2"), new("priority", "High", "3")]);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Save(Func<Task> action)
            {
                await gate.Task;
                try { await action(); return true; }
                catch (ApiError error) when (error.Status == 409) { return false; }
            }
            var githubSave = Save(() => s.Settings.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, s.Input, default));
            var jiraSave = Save(() => jira.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, input, default));
            gate.SetResult();
            Assert.Single(await Task.WhenAll(githubSave, jiraSave), saved => saved);
            Assert.Equal(1, await s.Db.Set<GitHubConnectionEntity>().CountAsync(c => c.WorkspaceId == s.Connection.WorkspaceId)
                + await s.Db.Set<JiraConnectionEntity>().CountAsync(c => c.WorkspaceId == s.Connection.WorkspaceId));
        }
    }

    [Fact]
    public async Task InboundConnectionsCanChooseTheirFirstOutboundRepositoryAfterImporting()
    {
        var s = await Setup("github-to-kanbada");
        using (s.Scope)
        {
            await s.Db.Set<GitHubConnectionEntity>().Where(c => c.Id == s.Connection.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.Repository, "").SetProperty(c => c.RepositoryId, ""));
            s.Db.ChangeTracker.Clear();
            s.Remote.Add("ONE");
            await Run(s);
            await s.Settings.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, s.Input with { Version = 1, Direction = "bidirectional" }, default);
            var saved = await s.Settings.Find(s.Connection.WorkspaceId, s.Connection.ProjectId, default);
            Assert.Equal("REPO", saved!.RepositoryId);
            Assert.Equal("bidirectional", saved.Direction);
        }
    }

    [Fact]
    public async Task JiraAndGitHubCannotShareAProject()
    {
        var s = await Setup();
        using (s.Scope)
        {
            var jiraSettings = s.Scope.ServiceProvider.GetRequiredService<JiraSettings>();
            var jiraInput = new JiraConnectionInput(0, "https://example.atlassian.net", "cloud", "test@example.test", "jira-token", "project = TEST",
                "TEST", "1", "jira-to-kanbada", "* * * * *", "UTC", true,
                [new("status", "backlog", "1"), new("priority", "Low", "1"), new("priority", "Medium", "2"), new("priority", "High", "3")]);
            var error = await Assert.ThrowsAsync<ApiError>(() => jiraSettings.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, jiraInput, default));
            Assert.Equal(409, error.Status);
            await s.Db.Set<GitHubConnectionEntity>().Where(c => c.Id == s.Connection.Id).ExecuteDeleteAsync();
            s.Db.ChangeTracker.Clear();
            await jiraSettings.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, jiraInput, default);
            error = await Assert.ThrowsAsync<ApiError>(() => s.Settings.Save(s.Connection.WorkspaceId, s.Connection.ProjectId, s.Input, default));
            Assert.Equal(409, error.Status);
        }
    }
}
