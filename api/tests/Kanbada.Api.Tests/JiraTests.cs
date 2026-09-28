using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

public sealed class JiraUnitTests
{
    [Theory]
    [InlineData("jira-to-kanbada", "kanbada", "jira")]
    [InlineData("kanbada-to-jira", "jira", "kanbada")]
    [InlineData("bidirectional", "jira", "jira")]
    [InlineData("bidirectional", "kanbada", "kanbada")]
    public void DirectionAndCreationOriginResolveConflicts(string direction, string origin, string expected) =>
        Assert.Equal(expected, JiraSnapshot.Winner(direction, origin, "local-new", "remote-new", "old", "old"));

    [Fact]
    public void SingleSidedChangesPropagateRegardlessOfOrigin()
    {
        Assert.Equal("kanbada", JiraSnapshot.Winner("bidirectional", "jira", "new", "old", "old", "old"));
        Assert.Equal("jira", JiraSnapshot.Winner("bidirectional", "kanbada", "old", "new", "old", "old"));
        Assert.Equal("none", JiraSnapshot.Winner("bidirectional", "jira", "same", "same", null, null));
    }

    [Theory]
    [InlineData("*/15 * * * *", "2026-01-01T00:15:00Z")]
    [InlineData("0 */2 * * *", "2026-01-01T02:00:00Z")]
    [InlineData("0 0 * * *", "2026-01-02T00:00:00Z")]
    [InlineData("0 0 1 * *", "2026-02-01T00:00:00Z")]
    [InlineData("0 0 1 */3 *", "2026-04-01T00:00:00Z")]
    public void CronSupportsMinutesHoursDaysAndCalendarMonths(string cron, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected), JiraSchedule.Next(cron, "UTC", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));

    [Fact]
    public void CronUsesTimezoneAndSkipsMissingCalendarDays()
    {
        Assert.Equal(TimeSpan.Zero, JiraSchedule.Next("0 9 * * *", "Europe/Lisbon", DateTimeOffset.Parse("2026-07-01T00:00:00Z")).Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T08:00:00Z"), JiraSchedule.Next("0 9 * * *", "Europe/Lisbon", DateTimeOffset.Parse("2026-07-01T00:00:00Z")));
        Assert.Equal(DateTimeOffset.Parse("2026-03-31T00:00:00Z"), JiraSchedule.Next("0 0 31 * *", "UTC", DateTimeOffset.Parse("2026-02-01T00:00:00Z")));
        Assert.Throws<ApiError>(() => JiraSchedule.Next("not cron", "UTC", DateTimeOffset.UtcNow));
        Assert.Throws<ApiError>(() => JiraSchedule.Next("* * * * *", "not/a/zone", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UrlPolicyRequiresHttpsAndExplicitPrivateHostApproval()
    {
        var policy = new JiraDestinationPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jira:AllowedHosts"] = "jira.example.test:8443"
        }).Build());
        Assert.Equal("https://jira.example.test:8443/jira/", policy.Validate("https://jira.example.test:8443/jira", "data-center").AbsoluteUri);
        Assert.Equal("team.atlassian.net", policy.Validate("https://team.atlassian.net", "cloud").Host);
        foreach (var url in new[] { "http://team.atlassian.net", "https://127.0.0.1", "https://user:secret@team.atlassian.net", "https://team.atlassian.net.evil.test", "https://team.atlassian.net/?x=1" })
            Assert.Throws<ApiError>(() => policy.Validate(url, "cloud"));
    }

    [Fact]
    public void CloudDescriptionAndLabelNormalizationAreStable()
    {
        var text = "First line\nSecond line";
        Assert.Equal(text, JiraSnapshot.Description(JiraClient.ToDocument(text)));
        var card = new CardEntity { Title = "Card", Description = text + "\r\n", StatusId = "backlog", Priority = "Medium" };
        Assert.Equal(JiraSnapshot.FromCard(card, ["b", "a"]).Hash(), JiraSnapshot.FromCard(card, ["a", "b", "a"]).Hash());
        Assert.Equal(JiraSnapshot.FromCard(card, ["Frontend", "ADMO"]).Hash(), JiraSnapshot.FromCard(card, [" frontend ", "admo", "ADMO"]).Hash());
    }
}

public sealed class JiraTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private async Task<(IServiceScope Scope, KanbadaDbContext Db, JiraConnectionEntity Connection, FakeJira Remote, JiraSyncEngine Engine)> Setup(string edition = "data-center", string direction = "bidirectional")
    {
        using var client = fixture.Client();
        var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        var owner = new UserEntity { Id = Guid.NewGuid(), Name = "Jira Owner", Email = Guid.NewGuid() + "@example.test" };
        db.Add(owner);
        await db.SaveChangesAsync();
        var workspace = await scope.ServiceProvider.GetRequiredService<WorkspaceStore>().Create(owner.Id, "Jira " + Guid.NewGuid());
        var secrets = scope.ServiceProvider.GetRequiredService<JiraSecrets>();
        var policy = new JiraDestinationPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jira:AllowedHosts"] = "jira.example.test"
        }).Build());
        var settings = new JiraSettings(db, scope.ServiceProvider.GetRequiredService<WorkspaceResolver>(), secrets, policy);
        var input = new JiraConnectionInput(0, "https://jira.example.test", edition, "jira@example.test", "test-pat",
            "project = TEAM ORDER BY key", "TEAM", "10001", direction, "* * * * *", "Europe/Lisbon", true,
            [new("status", "backlog", "10"), new("priority", "Low", "1"), new("priority", "Medium", "2"), new("priority", "High", "3")]);
        Assert.Equal(workspace, await settings.Authorize(workspace.ToString(), "my-activities", owner.Id, Ct));
        await Assert.ThrowsAsync<ApiError>(() => settings.Authorize(workspace.ToString(), "my-activities", Guid.NewGuid(), Ct));
        await settings.Save(workspace, "my-activities", input, Ct);
        var c = (await settings.Find(workspace, "my-activities", Ct))!;
        var publicJson = JsonSerializer.Serialize(await settings.Read(workspace, "my-activities", Ct));
        Assert.DoesNotContain("test-pat", publicJson);
        Assert.DoesNotContain(c.ProtectedToken, publicJson);
        Assert.Equal("test-pat", secrets.Unprotect(c.ProtectedToken));
        var remote = new FakeJira(edition);
        var jira = new JiraClient(new HttpClient(remote), secrets, policy);
        var engine = new JiraSyncEngine(db, scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(), jira, NullLogger<JiraSyncEngine>.Instance);
        return (scope, db, c, remote, engine);
    }

    private static async Task Run(KanbadaDbContext db, JiraConnectionEntity c, JiraSyncEngine engine)
    {
        db.ChangeTracker.Clear();
        await db.Set<JiraConnectionEntity>().Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await engine.Run(c.Id, Ct);
        db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData("data-center")]
    [InlineData("cloud")]
    public async Task ImportsReuseLabelsAndDoNotRepeatCaseOnlyChanges(string edition)
    {
        var (scope, db, c, remote, engine) = await Setup(edition);
        using (scope)
        {
            db.Add(new LabelEntity { WorkspaceId = c.WorkspaceId, Id = "frontend", Name = "Frontend", Color = "#123456" });
            await db.SaveChangesAsync();
            remote.Add("101", "First");
            remote.Add("102", "Second");
            remote.Issues["101"]["fields"]!["labels"] = new JsonArray("frontend", "Frontend", " frontend ", "ADMO", "admo");
            remote.Issues["102"]["fields"]!["labels"] = new JsonArray("FRONTEND", "admo");
            await Run(db, c, engine);
            Assert.Null((await db.Set<JiraConnectionEntity>().SingleAsync(x => x.Id == c.Id)).LastError);
            var labels = await db.Labels.Where(l => l.WorkspaceId == c.WorkspaceId).ToListAsync();
            Assert.Equal(2, labels.Count);
            Assert.Contains(labels, l => l.Id == "frontend" && l.Name == "Frontend" && l.Color == "#123456");
            Assert.Equal(4, await db.CardLabels.CountAsync(l => l.WorkspaceId == c.WorkspaceId));
            var version = await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.Version).SingleAsync();
            await Run(db, c, engine);
            Assert.Equal(version, await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.Version).SingleAsync());
            Assert.Equal(0, remote.Writes);
            Assert.Null((await db.Set<JiraConnectionEntity>().SingleAsync(x => x.Id == c.Id)).LastError);
        }
    }

    [Fact]
    public async Task InboundCardLocksDoNotBlockTheWorkerOrAllowWorkspaceDeletion()
    {
        var (scope, db, c, remote, engine) = await Setup(direction: "jira-to-kanbada");
        using (scope)
        {
            remote.Add("101", "Original");
            await Run(db, c, engine);
            remote.Issues["101"]["fields"]!["summary"] = "Updated in Jira";
            await Run(db, c, engine);
            Assert.Equal("Updated in Jira", (await db.Cards.SingleAsync(x => x.WorkspaceId == c.WorkspaceId)).Title);
            await db.Set<JiraConnectionEntity>().Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, false));
            var owner = await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.OwnerId).SingleAsync();
            var store = scope.ServiceProvider.GetRequiredService<WorkspaceStore>();
            var state = await store.Read(c.WorkspaceId, owner);
            Assert.True(state.State["tasks"]![0]!["readOnly"]!.GetValue<bool>());
            var error = await Assert.ThrowsAsync<ApiError>(() => store.Delete(c.WorkspaceId, owner));
            Assert.Equal(403, error.Status);
            await store.Save(c.WorkspaceId, owner, state.State, state.Version);
        }
    }

    [Fact]
    public async Task ExistingMappingsKeepTheirDefaultsAfterUpgrade()
    {
        var isolated = new ApiFixture();
        await isolated.InitializeAsync();
        try
        {
            var (scope, db, c, remote, engine) = await new JiraTests(isolated).Setup();
            using (scope)
            {
                var before = c.Mappings.Select(m => (m.Kind, m.KanbadaValue, m.JiraValue)).OrderBy(m => m.Kind).ThenBy(m => m.JiraValue).ToArray();
                await db.GetService<IMigrator>().MigrateAsync("20260925103601_UniqueWorkspaceLabels");
                await db.Database.MigrateAsync();
                db.ChangeTracker.Clear();
                var saved = await db.Set<JiraConnectionEntity>().Include(x => x.Mappings).SingleAsync(x => x.Id == c.Id);
                Assert.False(saved.SyncAssignees);
                Assert.All(saved.Mappings, m => Assert.True(m.IsDefault));
                Assert.Equal(before, saved.Mappings.Select(m => (m.Kind, m.KanbadaValue, m.JiraValue)).OrderBy(m => m.Kind).ThenBy(m => m.JiraValue).ToArray());
            }
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task FlexibleStatusMappingsRoundTripAndPreserveMatchingJiraStatuses()
    {
        var (scope, db, c, remote, engine) = await Setup();
        using (scope)
        {
            var policy = new JiraDestinationPolicy(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jira:AllowedHosts"] = "jira.example.test" }).Build());
            var settings = new JiraSettings(db, scope.ServiceProvider.GetRequiredService<WorkspaceResolver>(),
                scope.ServiceProvider.GetRequiredService<JiraSecrets>(), policy);
            var input = new JiraConnectionInput(c.Version, c.BaseUrl, c.Edition, c.Email, "", c.Jql,
                c.JiraProjectKey, c.IssueTypeId, c.Direction, c.Cron, c.TimeZone, true,
                c.Mappings.Select(m => new JiraMappingInput(m.Kind, m.KanbadaValue, m.JiraValue)).Append(
                    new JiraMappingInput("status", "backlog", "11", false)).ToList());
            await settings.Save(c.WorkspaceId, c.ProjectId, input, Ct);
            db.ChangeTracker.Clear();
            c = (await settings.Find(c.WorkspaceId, c.ProjectId, Ct))!;
            Assert.Equal(2, c.Mappings.Count(m => m.Kind == "status"));
            Assert.Equal("10", JiraSnapshot.ToJira(c, "status", "backlog"));
            var bad = input with { Version = c.Version, Mappings = input.Mappings.Select(m => m with { IsDefault = true }).ToList() };
            await Assert.ThrowsAsync<ApiError>(() => settings.Save(c.WorkspaceId, c.ProjectId, bad, Ct));
            bad = input with { Version = c.Version, Mappings = input.Mappings.Append(new JiraMappingInput("status", "other", "11")).ToList() };
            await Assert.ThrowsAsync<ApiError>(() => settings.Save(c.WorkspaceId, c.ProjectId, bad, Ct));
            bad = input with { Version = c.Version, Mappings = input.Mappings.Append(new JiraMappingInput("assignee", Guid.NewGuid().ToString(), "jira-user")).ToList() };
            await Assert.ThrowsAsync<ApiError>(() => settings.Save(c.WorkspaceId, c.ProjectId, bad, Ct));
            bad = input with { Version = c.Version, Mappings = input.Mappings.Select(m => m with { IsDefault = false }).ToList() };
            await Assert.ThrowsAsync<ApiError>(() => settings.Save(c.WorkspaceId, c.ProjectId, bad, Ct));
            remote.Add("101", "Additional status");
            remote.Issues["101"]["fields"]!["status"] = new JsonObject { ["id"] = "11", ["name"] = "Selected" };
            await Run(db, c, engine);
            var card = await db.Cards.SingleAsync(x => x.WorkspaceId == c.WorkspaceId);
            Assert.Equal("backlog", card.StatusId);
            card.Title = "Local change";
            await db.SaveChangesAsync();
            await Run(db, c, engine);
            Assert.Equal("Local change", remote.Issues["101"]["fields"]!["summary"]!.GetValue<string>());
            Assert.Equal("11", remote.Issues["101"]["fields"]!["status"]!["id"]!.GetValue<string>());
            Assert.Equal(0, remote.Transitions);
            var jira = new JiraClient(new HttpClient(remote), scope.ServiceProvider.GetRequiredService<JiraSecrets>(), policy);
            var metadata = JsonSerializer.SerializeToNode(await jira.Metadata(c, Ct))!;
            Assert.Equal(2, metadata["Statuses"]!.AsArray().Count);
            remote.Issues["101"]["fields"]!["status"] = new JsonObject { ["id"] = "99" };
            await jira.Transition(c, new JiraIssue("101", "TEAM-101", remote.Issues["101"]["fields"]!.AsObject()), "backlog", Ct);
            Assert.Equal("10", remote.Issues["101"]["fields"]!["status"]!["id"]!.GetValue<string>());
            Assert.Equal(1, remote.Transitions);
        }
    }

    [Theory]
    [InlineData("cloud")]
    [InlineData("data-center")]
    public async Task SavedMappingNamesLoadOutsideTheCurrentJql(string edition)
    {
        var (scope, db, c, remote, engine) = await Setup(edition);
        using (scope)
        {
            c.Mappings.Add(new JiraMappingEntity { ConnectionId = c.Id, Kind = "status", KanbadaValue = "backlog", JiraValue = "11", IsDefault = false });
            c.Mappings.Add(new JiraMappingEntity { ConnectionId = c.Id, Kind = "assignee", KanbadaValue = Guid.NewGuid().ToString(), JiraValue = "saved-user" });
            c.Mappings.Add(new JiraMappingEntity { ConnectionId = c.Id, Kind = "assignee", KanbadaValue = Guid.NewGuid().ToString(), JiraValue = "missing" });
            var policy = new JiraDestinationPolicy(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jira:AllowedHosts"] = "jira.example.test" }).Build());
            var jira = new JiraClient(new HttpClient(remote), scope.ServiceProvider.GetRequiredService<JiraSecrets>(), policy);
            var metadata = JsonSerializer.SerializeToNode(await jira.Metadata(c, Ct))!;
            Assert.Contains(metadata["Statuses"]!.AsArray(), s => s!["Name"]!.ToString() == "Selected");
            Assert.Equal("Saved Jira User", Assert.Single(metadata["Assignees"]!.AsArray())!["Name"]!.ToString());
            Assert.Single(metadata["Warnings"]!.AsArray());
        }
    }

    [Theory]
    [InlineData("cloud")]
    [InlineData("data-center")]
    public async Task AssigneesImportIndependentlyAndUnmappedUsersClearWithWarnings(string edition)
    {
        var (scope, db, c, remote, engine) = await Setup(edition);
        using (scope)
        {
            var owner = await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.OwnerId).SingleAsync();
            var email = await db.Members.Where(m => m.WorkspaceId == c.WorkspaceId && m.UserId == owner).Select(m => m.Email).SingleAsync();
            c.SyncAssignees = true;
            c.Mappings.Add(new JiraMappingEntity { ConnectionId = c.Id, Kind = "assignee", JiraValue = "jira-user", KanbadaValue = owner.ToString(), IsDefault = false });
            await db.SaveChangesAsync();
            remote.Add("101", "Assigned");
            remote.Issues["101"]["fields"]!["assignee"] = new JsonObject { ["accountId"] = "jira-user", ["key"] = "jira-user", ["name"] = "username", ["displayName"] = "Jira User" };
            await Run(db, c, engine);
            var card = await db.Cards.SingleAsync(x => x.WorkspaceId == c.WorkspaceId);
            Assert.Equal(email, (await db.CardAssignees.SingleAsync(a => a.WorkspaceId == c.WorkspaceId)).MemberEmail);
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.WorkspaceId == c.WorkspaceId && n.RecipientId == owner));
            var version = await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.Version).SingleAsync();
            await Run(db, c, engine);
            Assert.Equal(version, await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.Version).SingleAsync());
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.WorkspaceId == c.WorkspaceId && n.RecipientId == owner));
            remote.Issues["101"]["fields"]!["assignee"] = new JsonObject { ["accountId"] = "unmapped", ["key"] = "unmapped" };
            await Run(db, c, engine);
            Assert.Empty(await db.CardAssignees.Where(a => a.WorkspaceId == c.WorkspaceId).ToListAsync());
            var link = await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id);
            Assert.Contains("left unassigned", link.LastError);
            Assert.NotNull((await db.Set<JiraConnectionEntity>().SingleAsync(x => x.Id == c.Id)).LastError);
            Assert.Equal(version + 1, await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.Version).SingleAsync());
            c = (await db.Set<JiraConnectionEntity>().Include(x => x.Mappings).SingleAsync(x => x.Id == c.Id));
            c.Mappings.Add(new JiraMappingEntity { ConnectionId = c.Id, Kind = "assignee", JiraValue = "unmapped", KanbadaValue = owner.ToString() });
            await db.SaveChangesAsync();
            await Run(db, c, engine);
            Assert.Equal(email, (await db.CardAssignees.SingleAsync(a => a.WorkspaceId == c.WorkspaceId)).MemberEmail);
            Assert.Null((await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id)).LastError);
            remote.Issues["101"]["fields"]!["assignee"] = null;
            await Run(db, c, engine);
            Assert.Empty(await db.CardAssignees.Where(a => a.WorkspaceId == c.WorkspaceId).ToListAsync());
            Assert.Null((await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id)).LastError);
            Assert.Equal(0, remote.Writes);

            db.Add(new CardAssigneeEntity { WorkspaceId = c.WorkspaceId, CardId = card.Id, MemberEmail = email });
            await db.Set<JiraConnectionEntity>().Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Direction, "kanbada-to-jira"));
            await db.SaveChangesAsync();
            await Run(db, c, engine);
            Assert.Equal(email, (await db.CardAssignees.SingleAsync(a => a.WorkspaceId == c.WorkspaceId)).MemberEmail);
            await db.Set<JiraConnectionEntity>().Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Direction, "bidirectional").SetProperty(x => x.SyncAssignees, false));
            remote.Issues["101"]["fields"]!["summary"] = "Keep local members when disabled";
            await Run(db, c, engine);
            Assert.Equal(email, (await db.CardAssignees.SingleAsync(a => a.WorkspaceId == c.WorkspaceId)).MemberEmail);
        }
    }

    [Fact]
    public async Task CardLinksRequireMembershipAndPreserveContextPaths()
    {
        var (scope, db, c, remote, engine) = await Setup();
        using (scope)
        {
            remote.Add("101", "Linked card");
            await Run(db, c, engine);
            var link = await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id);
            var connection = await db.Set<JiraConnectionEntity>().SingleAsync(x => x.Id == c.Id);
            connection.BaseUrl = "https://jira.example.test:8443/jira/";
            connection.Enabled = false;
            var member = new UserEntity { Id = Guid.NewGuid(), Email = "member-" + Guid.NewGuid() + "@example.test", Name = "Member" };
            db.Add(member);
            db.Add(new MemberEntity { WorkspaceId = c.WorkspaceId, Email = member.Email, UserId = member.Id, Name = member.Name });
            await db.SaveChangesAsync();
            var resolver = scope.ServiceProvider.GetRequiredService<WorkspaceResolver>();
            var jira = scope.ServiceProvider.GetRequiredService<JiraClient>();
            var links = await JiraEndpoints.CardLinks(c.WorkspaceId.ToString(), link.CardId, member.Id, resolver, db, jira, Ct);
            Assert.Equal(new JiraEndpoints.CardLink("TEAM-101", "https://jira.example.test:8443/jira/browse/TEAM-101"), Assert.Single(links));
            await Assert.ThrowsAsync<ApiError>(() => JiraEndpoints.CardLinks(c.WorkspaceId.ToString(), link.CardId, Guid.NewGuid(), resolver, db, jira, Ct));
            await Assert.ThrowsAsync<ApiError>(() => JiraEndpoints.CardLinks(c.WorkspaceId.ToString(), "KB-MISSING", member.Id, resolver, db, jira, Ct));
            link.CreationPending = true;
            await db.SaveChangesAsync();
            Assert.Empty(await JiraEndpoints.CardLinks(c.WorkspaceId.ToString(), link.CardId, member.Id, resolver, db, jira, Ct));
            db.Remove(link);
            await db.SaveChangesAsync();
            Assert.Empty(await JiraEndpoints.CardLinks(c.WorkspaceId.ToString(), link.CardId, member.Id, resolver, db, jira, Ct));
        }
    }

    [Fact]
    public async Task ScopedCloudLinksUseTheBrowserSite()
    {
        var (scope, db, c, remote, engine) = await Setup("cloud");
        using (scope)
        {
            c.BaseUrl = "https://api.atlassian.com/ex/jira/cloud-id";
            var policy = new JiraDestinationPolicy(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jira:AllowedHosts"] = "api.atlassian.com" }).Build());
            var jira = new JiraClient(new HttpClient(remote), scope.ServiceProvider.GetRequiredService<JiraSecrets>(), policy);
            Assert.Equal("https://team.atlassian.net/browse/TEAM-101", await jira.BrowseUrl(c, "TEAM-101", Ct));
            c.BaseUrl = "https://team.atlassian.net";
            Assert.Equal("https://team.atlassian.net/browse/TEAM-101", await jira.BrowseUrl(c, "TEAM-101", Ct));
        }
    }

    [Theory]
    [InlineData("data-center")]
    [InlineData("cloud")]
    public async Task ImportsAllPagesPreservesLinksAndResolvesJiraOriginConflicts(string edition)
    {
        var (scope, db, c, remote, engine) = await Setup(edition);
        using (scope)
        {
            remote.Add("101", "First");
            remote.Add("102", "Second");
            var before = await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.Version).SingleAsync();
            await Run(db, c, engine);
            Assert.Equal(2, await db.Set<JiraLinkEntity>().CountAsync(l => l.ConnectionId == c.Id));
            Assert.Equal(before + 2, await db.Workspaces.Where(w => w.Id == c.WorkspaceId).Select(w => w.Version).SingleAsync());
            Assert.Equal(edition == "cloud" ? "Basic" : "Bearer", remote.Scheme);
            Assert.True(remote.Pages >= 2);
            var link = await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id && l.JiraIssueId == "101");
            var card = await db.Cards.SingleAsync(x => x.WorkspaceId == c.WorkspaceId && x.Id == link.CardId);
            Assert.Equal("jira", link.Origin);
            Assert.Equal(new DateOnly(2026, 12, 1), card.Due);
            Assert.Equal(1, await db.CardLabels.CountAsync(l => l.WorkspaceId == c.WorkspaceId && l.CardId == card.Id));
            card.Title = "Local edit";
            await db.SaveChangesAsync();
            remote.Issues["101"]["fields"]!["summary"] = "Remote edit";
            await Run(db, c, engine);
            Assert.Equal("Remote edit", (await db.Cards.SingleAsync(x => x.WorkspaceId == c.WorkspaceId && x.Id == link.CardId)).Title);
            Assert.Equal(0, remote.Writes);
            await Run(db, c, engine);
            Assert.Equal(2, await db.Cards.CountAsync(x => x.WorkspaceId == c.WorkspaceId));
            Assert.Null((await db.Set<JiraConnectionEntity>().SingleAsync(x => x.Id == c.Id)).LastError);
        }
    }

    [Theory]
    [InlineData("jira-to-kanbada", "Remote edit", "Remote edit")]
    [InlineData("kanbada-to-jira", "Local edit", "Local edit")]
    [InlineData("bidirectional", "Local edit", "Local edit")]
    public async Task KanbadaOriginAndOneWayModesRespectDirection(string direction, string expectedLocal, string expectedRemote)
    {
        var (scope, db, c, remote, engine) = await Setup();
        using (scope)
        {
            db.Cards.Add(new CardEntity { WorkspaceId = c.WorkspaceId, Id = "KB-EXPORT", ProjectId = c.ProjectId, Title = "Original", Priority = "Medium", StatusId = "backlog" });
            await db.SaveChangesAsync();
            await Run(db, c, engine);
            Assert.Equal(1, remote.Creates);
            var link = await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id);
            Assert.Equal("kanbada", link.Origin);
            var card = await db.Cards.SingleAsync(x => x.WorkspaceId == c.WorkspaceId && x.Id == link.CardId);
            card.Title = "Local edit";
            var settings = await db.Set<JiraConnectionEntity>().SingleAsync(x => x.Id == c.Id);
            settings.Direction = direction;
            await db.SaveChangesAsync();
            remote.Issues[link.JiraIssueId!]["fields"]!["summary"] = "Remote edit";
            await Run(db, c, engine);
            Assert.Equal(expectedLocal, (await db.Cards.SingleAsync(x => x.WorkspaceId == c.WorkspaceId && x.Id == link.CardId)).Title);
            Assert.Equal(expectedRemote, remote.Issues[link.JiraIssueId!]["fields"]!["summary"]!.GetValue<string>());
            Assert.Equal(1, remote.Creates);
        }
    }

    [Fact]
    public async Task AmbiguousCreateCannotDuplicateIssuesOrImportDuplicateCards()
    {
        var (scope, db, c, remote, engine) = await Setup();
        using (scope)
        {
            remote.FailCreateAfterCommit = true;
            db.Cards.Add(new CardEntity { WorkspaceId = c.WorkspaceId, Id = "KB-UNCERTAIN", ProjectId = c.ProjectId, Title = "Uncertain", Priority = "Medium", StatusId = "backlog" });
            await db.SaveChangesAsync();
            await Run(db, c, engine);
            await Run(db, c, engine);
            Assert.Equal(1, remote.Creates);
            Assert.Single(remote.Issues);
            Assert.Equal(1, await db.Cards.CountAsync(x => x.WorkspaceId == c.WorkspaceId));
            var link = await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id);
            Assert.True(link.CreationPending);
            Assert.NotNull(link.LastError);
            link.JiraIssueId = remote.Issues.Keys.Single();
            link.CreationPending = false;
            await db.SaveChangesAsync();
            await Run(db, c, engine);
            Assert.Equal(1, remote.Creates);
            Assert.Null((await db.Set<JiraLinkEntity>().SingleAsync(l => l.Id == link.Id)).LastError);
        }
    }

    [Fact]
    public async Task DeletedCardsAreNotRecreatedAndJqlExclusionsAreNotDeleted()
    {
        var (scope, db, c, remote, engine) = await Setup();
        using (scope)
        {
            remote.Add("101", "Delete locally");
            remote.Add("102", "Leave query");
            await Run(db, c, engine);
            var link = await db.Set<JiraLinkEntity>().SingleAsync(l => l.ConnectionId == c.Id && l.JiraIssueId == "101");
            await db.Cards.Where(x => x.WorkspaceId == c.WorkspaceId && x.Id == link.CardId).ExecuteDeleteAsync();
            remote.Hidden.Add("102");
            await Run(db, c, engine);
            Assert.Equal(1, await db.Cards.CountAsync(x => x.WorkspaceId == c.WorkspaceId));
            Assert.Equal(2, remote.Issues.Count);
            Assert.Equal(2, await db.Set<JiraLinkEntity>().CountAsync(l => l.ConnectionId == c.Id && l.LastError != null));
        }
    }

    [Fact]
    public async Task ArchivedAndDisabledProjectsDoNotMakeRemoteRequests()
    {
        var (scope, db, c, remote, engine) = await Setup();
        using (scope)
        {
            await db.Projects.Where(p => p.WorkspaceId == c.WorkspaceId && p.Id == c.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Archived, true));
            await Run(db, c, engine);
            Assert.Equal(0, remote.Pages);
            await db.Projects.Where(p => p.WorkspaceId == c.WorkspaceId && p.Id == c.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Archived, false));
            await db.Set<JiraConnectionEntity>().Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, false));
            await Run(db, c, engine);
            Assert.Equal(0, remote.Pages);
        }
    }

    [Theory]
    [InlineData("cloud")]
    [InlineData("data-center")]
    public async Task DiscoversMetadataUsingSupportedEditionEndpoints(string edition)
    {
        var (scope, db, c, remote, engine) = await Setup(edition);
        using (scope)
        {
            var policy = new JiraDestinationPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jira:AllowedHosts"] = "jira.example.test" }).Build());
            var client = new JiraClient(new HttpClient(remote), scope.ServiceProvider.GetRequiredService<JiraSecrets>(), policy);
            var metadata = JsonSerializer.SerializeToNode(await client.Metadata(c, Ct))!;
            Assert.Single(metadata["IssueTypes"]!.AsArray());
            Assert.Single(metadata["Statuses"]!.AsArray());
            Assert.Equal(3, metadata["Priorities"]!.AsArray().Count);
            Assert.Equal(0, remote.Writes);
            Assert.Equal(0, remote.Creates);
        }
    }

    private sealed class FakeJira(string edition) : HttpMessageHandler
    {
        public Dictionary<string, JsonObject> Issues { get; } = [];
        public HashSet<string> Hidden { get; } = [];
        public string? Scheme { get; private set; }
        public int Pages { get; private set; }
        public int Writes { get; private set; }
        public int Creates { get; private set; }
        public int Transitions { get; private set; }
        public bool FailCreateAfterCommit { get; set; }

        public void Add(string id, string title)
        {
            Issues[id] = new JsonObject
            {
                ["id"] = id,
                ["key"] = "TEAM-" + id,
                ["fields"] = new JsonObject
                {
                    ["summary"] = title,
                    ["description"] = edition == "cloud" ? JiraClient.ToDocument("Description") : JsonValue.Create("Description"),
                    ["status"] = new JsonObject { ["id"] = "10" },
                    ["priority"] = new JsonObject { ["id"] = "2" },
                    ["labels"] = new JsonArray("jira-label"),
                    ["duedate"] = "2026-12-01",
                    ["assignee"] = null,
                    ["project"] = new JsonObject { ["key"] = "TEAM" }
                }
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Scheme = request.Headers.Authorization?.Scheme;
            var url = request.RequestUri!.AbsolutePath;
            if (url.EndsWith("/serverInfo"))
                return Json(new JsonObject { ["baseUrl"] = "https://team.atlassian.net" });
            if (url.EndsWith("/status/11"))
                return Json(new JsonObject { ["id"] = "11", ["name"] = "Selected" });
            if (url.EndsWith("/user"))
            {
                Assert.True(edition == "cloud" ? request.RequestUri.Query.Contains("accountId=")
                    : request.RequestUri.Query.Contains("key=") || request.RequestUri.Query.Contains("username="));
                if (request.RequestUri.Query.Contains("missing")) return new HttpResponseMessage(HttpStatusCode.NotFound);
                return Json(new JsonObject { ["displayName"] = "Saved Jira User" });
            }
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct));
            if (url.EndsWith("/statuses"))
                return Json(new JsonArray(new JsonObject
                {
                    ["id"] = "10001",
                    ["name"] = "Task",
                    ["statuses"] = new JsonArray(new JsonObject { ["id"] = "10", ["name"] = "Backlog" })
                }));
            if (url.EndsWith("/priority/search"))
            {
                Assert.Equal("cloud", edition);
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                var index = int.Parse(query["startAt"].ToString());
                return Json(new JsonObject
                {
                    ["total"] = 3,
                    ["isLast"] = index == 2,
                    ["values"] = new JsonArray(new JsonObject { ["id"] = (index + 1).ToString(), ["name"] = "Priority " + index })
                });
            }
            if (url.EndsWith("/priority"))
            {
                Assert.Equal("data-center", edition);
                return Json(new JsonArray(Enumerable.Range(1, 3).Select(i => (JsonNode)new JsonObject { ["id"] = i.ToString(), ["name"] = "Priority " + i }).ToArray()));
            }
            if (url.EndsWith("/search") || url.EndsWith("/search/jql"))
            {
                Pages++;
                Assert.Equal("project = TEAM ORDER BY key", body!["jql"]!.GetValue<string>());
                var index = edition == "cloud" ? int.Parse(body["nextPageToken"]?.GetValue<string>() ?? "0") : body["startAt"]!.GetValue<int>();
                var visible = Issues.Values.Where(i => !Hidden.Contains(i["id"]!.GetValue<string>())).ToList();
                return Json(new JsonObject
                {
                    ["issues"] = new JsonArray(visible.Skip(index).Take(1).Select(i => i.DeepClone()).ToArray()),
                    ["total"] = visible.Count,
                    ["isLast"] = index + 1 >= visible.Count,
                    ["nextPageToken"] = index + 1 < visible.Count ? (index + 1).ToString() : null
                });
            }
            if (url.EndsWith("/issue") && request.Method == HttpMethod.Post)
            {
                Assert.False(body!["fields"]!.AsObject().ContainsKey("assignee"));
                Creates++;
                var id = (1000 + Creates).ToString();
                Add(id, body!["fields"]!["summary"]!.GetValue<string>());
                foreach (var field in body["fields"]!.AsObject()) Issues[id]["fields"]![field.Key] = field.Value?.DeepClone();
                if (FailCreateAfterCommit) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return Json(new JsonObject { ["id"] = id, ["key"] = "TEAM-" + id });
            }
            var issueId = url.Split('/').Last();
            if (url.EndsWith("/transitions"))
            {
                if (request.Method == HttpMethod.Get)
                    return Json(new JsonObject { ["transitions"] = new JsonArray(new JsonObject { ["id"] = "to-10", ["to"] = new JsonObject { ["id"] = "10" } }) });
                Assert.Equal("to-10", body!["transition"]!["id"]!.GetValue<string>());
                Transitions++;
                Issues[url.Split('/')[^2]]["fields"]!["status"] = new JsonObject { ["id"] = "10" };
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get && Issues.TryGetValue(issueId, out var issue)) return Json(issue.DeepClone());
            if (request.Method == HttpMethod.Put && Issues.TryGetValue(issueId, out issue))
            {
                Assert.False(body!["fields"]!.AsObject().ContainsKey("assignee"));
                Writes++;
                foreach (var field in body!["fields"]!.AsObject()) issue["fields"]![field.Key] = field.Value?.DeepClone();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException("Unexpected fake Jira request: " + request.Method + " " + url);
        }

        private static HttpResponseMessage Json(JsonNode body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
    }
}
