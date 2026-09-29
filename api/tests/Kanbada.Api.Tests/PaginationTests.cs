using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ApiTests;

public sealed class PaginationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient Client, string Email)> User()
    {
        var (client, email, _) = await CreateUser(fixture);
        return (client, email);
    }
    internal static async Task<JsonObject> SeedPages(HttpClient client)
    {
        var state = await Body(await client.GetAsync("/api/workspaces/studio"));
        var member = state["members"]![0]!["name"]!.GetValue<string>();
        state["buckets"]!.AsArray().Add(new JsonObject { ["id"] = "paging", ["name"] = "Paging", ["color"] = "#123456", ["complete"] = false });
        state["swimlanes"]!.AsArray().Add(new JsonObject { ["id"] = "lane", ["name"] = "Lane", ["project"] = "my-activities", ["color"] = "#123456", ["complete"] = false });
        state["labels"]!.AsArray().Add(new JsonObject { ["id"] = "needle", ["name"] = "Needle", ["color"] = "#123456", ["complete"] = false });
        state["tasks"] = new JsonArray(Enumerable.Range(0, 125).Select(i => (JsonNode)new JsonObject
        {
            ["id"] = $"KB-PAGE-{i:D4}",
            ["title"] = $"Card {i}",
            ["description"] = new string('x', 2000),
            ["project"] = "my-activities",
            ["status"] = i % 5 == 0 ? "Done" : "Backlog",
            ["priority"] = i % 2 == 0 ? "High" : "Medium",
            ["due"] = i % 2 == 0 ? "2026-09-28" : "",
            ["bucket"] = i % 2 == 0 ? "Paging" : "",
            ["swimlane"] = i % 2 == 0 ? "Lane" : "",
            ["labels"] = i == 124 ? new JsonArray("Needle") : new JsonArray(),
            ["assignees"] = i % 2 == 0 ? new JsonArray(member) : new JsonArray(),
            ["comments"] = new JsonArray(),
            ["checklist"] = new JsonArray(new JsonObject { ["text"] = "Step", ["done"] = i % 2 == 0 }),
            ["attachments"] = new JsonArray()
        }).ToArray());
        return await Save(client, state);
    }

    private async Task<JsonObject> SavePartial(HttpClient client, JsonObject state, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/workspaces/studio/changes") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", state["version"]!.ToString());
        return await Body(await client.SendAsync(request));
    }

    [Fact]
    public async Task CardPagesAreBoundedOrderedFilteredAndAuthorized()
    {
        var (client, _) = await User();
        using (client)
        {
            var state = await SeedPages(client);
            var metadata = await Body(await client.GetAsync("/api/workspaces/studio?metadataOnly=true"));
            Assert.Empty(metadata["tasks"]!.AsArray());
            Assert.Equal(state["version"]!.ToString(), metadata["version"]!.ToString());
            var seen = new List<string>();
            string? cursor = null;
            do
            {
                var page = await Body(await client.GetAsync("/api/workspaces/studio/cards?limit=17" + (cursor is null ? "" : "&after=" + Uri.EscapeDataString(cursor))));
                Assert.InRange(page["items"]!.AsArray().Count, 1, 17);
                Assert.Equal(125, page["total"]!.GetValue<int>());
                seen.AddRange(page["items"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()));
                cursor = page["nextCursor"]?.GetValue<string>();
            } while (cursor is not null);
            Assert.Equal(125, seen.Distinct().Count());
            Assert.Equal(Enumerable.Range(0, 125).Select(i => $"KB-PAGE-{i:D4}"), seen);
            var filtered = await Body(await client.GetAsync("/api/workspaces/studio/cards?search=needle&bucket=Paging&swimlane=lane&priority=High&mine=true&completion=Overdue&today=2026-09-29"));
            Assert.Equal("KB-PAGE-0124", Assert.Single(filtered["items"]!.AsArray())!["id"]!.ToString());
            var phrase = await Body(await client.GetAsync("/api/workspaces/studio/cards?search=Card%20124%20Needle%20KB-PAGE-0124"));
            Assert.Single(phrase["items"]!.AsArray());
            var ungrouped = await Body(await client.GetAsync("/api/workspaces/studio/cards?bucket=&swimlane=&unassigned=true"));
            Assert.Equal(62, ungrouped["total"]!.GetValue<int>());
            var due = await Body(await client.GetAsync("/api/workspaces/studio/cards?from=2026-09-28&to=2026-09-28"));
            Assert.Equal(63, due["total"]!.GetValue<int>());
            var person = Uri.EscapeDataString(state["members"]![0]!["name"]!.GetValue<string>());
            Assert.Equal(63, (await Body(await client.GetAsync("/api/workspaces/studio/cards?person=" + person)))["total"]!.GetValue<int>());
            Assert.Equal(25, (await Body(await client.GetAsync("/api/workspaces/studio/cards?status=Done&completion=Completed")))["total"]!.GetValue<int>());
            var detail = await Body(await client.GetAsync("/api/workspaces/studio/cards/kb-page-0124"));
            Assert.Single(detail["history"]!.AsArray());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/workspaces/studio/cards?limit=101")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/workspaces/studio/cards?limit=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/workspaces/studio/cards?after=invalid")).StatusCode);
            var first = await Body(await client.GetAsync("/api/workspaces/studio/cards?limit=1"));
            cursor = Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/workspaces/studio/cards?limit=1&priority=High&after=" + cursor)).StatusCode);
            await Body(await client.PostAsJsonAsync("/api/workspaces/studio/cards", new { title = "New card" }));
            Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/workspaces/studio/cards?limit=1&after=" + cursor)).StatusCode);
            var (outsider, _) = await User();
            using (outsider)
            {
                using var scope = fixture.Factory.Services.CreateScope();
                var workspace = await scope.ServiceProvider.GetRequiredService<WorkspaceResolver>()
                    .WorkspaceId("studio", Guid.Parse(state["workspace"]!["ownerId"]!.GetValue<string>()));
                Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/workspaces/{workspace}/cards")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/workspaces/{workspace}/card-summary")).StatusCode);
            }
        }
    }

    [Fact]
    public async Task PartialSavesPreserveUnloadedCardsAndStructuralReferences()
    {
        var (client, _) = await User();
        using (client)
        {
            var original = await SeedPages(client);
            var metadata = await Body(await client.GetAsync("/api/workspaces/studio?metadataOnly=true"));
            var edited = await SavePartial(client, metadata, new
            {
                changes = Array.Empty<object>(),
                removed = Array.Empty<string>(),
                upserts = new[] { new CardEdit("KB-PAGE-0124", [new StateChange("replace", "/tasks/0/title", JsonValue.Create("Edited last card"))]) }
            });
            Assert.DoesNotContain(new string('x', 2000), edited.ToJsonString());
            var full = await Body(await client.GetAsync("/api/workspaces/studio"));
            Assert.Equal(125, full["tasks"]!.AsArray().Count);
            Assert.True(JsonNode.DeepEquals(original["tasks"]![0], full["tasks"]![0]));
            Assert.Equal("Edited last card", full["tasks"]![124]!["title"]!.ToString());
            Assert.Equal(2, full["tasks"]![124]!["history"]!.AsArray().Count);
            using (var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/workspaces/studio/changes"))
            {
                stale.Headers.TryAddWithoutValidation("If-Match", metadata["version"]!.ToString());
                stale.Content = JsonContent.Create(new { changes = Array.Empty<object>(), upserts = Array.Empty<CardEdit>(), removed = new[] { "KB-PAGE-0000" } });
                Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(stale)).StatusCode);
            }
            using (var malformed = new HttpRequestMessage(HttpMethod.Patch, "/api/workspaces/studio/changes"))
            {
                malformed.Headers.TryAddWithoutValidation("If-Match", full["version"]!.ToString());
                malformed.Content = JsonContent.Create(new { changes = new[] { new StateChange("remove", "invalid") }, upserts = Array.Empty<CardEdit>(), removed = Array.Empty<string>() });
                Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(malformed)).StatusCode);
            }
            var bucketIndex = full["buckets"]!.AsArray().ToList().FindIndex(b => b!["id"]!.ToString() == "paging");
            var renamed = await SavePartial(client, full, new
            {
                changes = new[] { new StateChange("replace", $"/buckets/{bucketIndex}/name", JsonValue.Create("Renamed")) },
                upserts = Array.Empty<CardEdit>(),
                removed = Array.Empty<string>(),
                retained = new[] { "KB-PAGE-0124" }
            });
            Assert.Equal("KB-PAGE-0124", Assert.Single(renamed["cards"]!.AsArray())!["id"]!.ToString());
            Assert.Contains("Renamed", renamed["cards"]![0]!["changes"]!.ToJsonString());
            full = await Body(await client.GetAsync("/api/workspaces/studio"));
            Assert.Equal(63, full["tasks"]!.AsArray().Count(c => c!["bucket"]!.ToString() == "Renamed"));
            await SavePartial(client, full, new { changes = Array.Empty<object>(), upserts = Array.Empty<CardEdit>(), removed = new[] { "KB-PAGE-0124" } });
            var page = await Body(await client.GetAsync("/api/workspaces/studio/cards"));
            Assert.Equal(124, page["total"]!.GetValue<int>());
            using var lookup = fixture.Factory.Services.CreateScope();
            var db = lookup.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            var workspace = await lookup.ServiceProvider.GetRequiredService<WorkspaceResolver>().WorkspaceId("studio", Guid.Parse(full["workspace"]!["ownerId"]!.GetValue<string>()));
            Assert.Equal(123, await db.Cards.Where(c => c.WorkspaceId == workspace && c.Id == "KB-PAGE-0123").Select(c => c.Position).SingleAsync());
            full = await Body(await client.GetAsync("/api/workspaces/studio"));
            full["projects"]!.AsArray().Add(new JsonObject { ["id"] = "temporary", ["name"] = "Temporary", ["color"] = "#123456", ["description"] = "" });
            full["tasks"]![0]!["project"] = "temporary";
            full["tasks"]![0]!["swimlane"] = "";
            full = await Save(client, full);
            var projectIndex = full["projects"]!.AsArray().ToList().FindIndex(p => p!["id"]!.ToString() == "temporary");
            await SavePartial(client, full, new
            {
                changes = new[] { new StateChange("remove", $"/projects/{projectIndex}") },
                upserts = Array.Empty<CardEdit>(),
                removed = Array.Empty<string>()
            });
            Assert.Equal(123, (await Body(await client.GetAsync("/api/workspaces/studio/cards")))["total"]!.GetValue<int>());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/workspaces/studio/cards/KB-PAGE-0000")).StatusCode);
        }
    }

    [Fact]
    public async Task CardSummariesIncludeUnloadedMatches()
    {
        var (client, _) = await User();
        using (client)
        {
            await SeedPages(client);
            var summary = await Body(await client.GetAsync("/api/workspaces/studio/card-summary?today=2026-09-29"));
            Assert.Equal(125, summary["counts"]!["total"]!.GetValue<int>());
            Assert.Equal(25, summary["counts"]!["completed"]!.GetValue<int>());
            Assert.Equal(50, summary["counts"]!["overdue"]!.GetValue<int>());
            Assert.Equal(125, summary["checklist"]!["total"]!.GetValue<int>());
            Assert.Equal(63, summary["checklist"]!["completed"]!.GetValue<int>());
            Assert.Equal(50, summary["myOpen"]!.GetValue<int>());
            Assert.Equal(5, summary["activity"]!.AsArray().Count);
            Assert.Contains("needle", summary["usedLabels"]!.AsArray().Select(l => l!.ToString()));
            var filtered = await Body(await client.GetAsync("/api/workspaces/studio/card-summary?bucket=Paging&swimlane=lane&today=2026-09-29"));
            Assert.Equal(63, filtered["counts"]!["total"]!.GetValue<int>());
            var empty = await Body(await client.GetAsync("/api/workspaces/studio/card-summary?search=absent"));
            Assert.Equal(0, empty["counts"]!["total"]!.GetValue<int>());
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync("/api/workspaces/studio/export")).StatusCode);
        }
    }

    [Fact]
    public async Task MillionCardWorkspaceMaterializesOnlyTheRequestedPage()
    {
        var (client, _) = await User();
        using (client)
        {
            var initial = await Body(await client.GetAsync("/api/workspaces/studio?metadataOnly=true"));
            var user = Guid.Parse(initial["workspace"]!["ownerId"]!.GetValue<string>());
            Guid workspace;
            using (var seed = fixture.Factory.Services.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<KanbadaDbContext>();
                workspace = await seed.ServiceProvider.GetRequiredService<WorkspaceResolver>().WorkspaceId("studio", user);
                var status = initial["statuses"]![0]!["id"]!.GetValue<string>();
                db.Database.SetCommandTimeout(180);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cards (workspace_id, id, project_id, status_id, title, description, priority, position)
                        SELECT {workspace}, 'KB-SCALE-' || lpad(n::text, 7, '0'), 'my-activities', {status}, 'Scale card ' || n, '', 'Medium', n
                        FROM generate_series(1, 1000000) AS n
                        """);
            }
            using (var read = fixture.Factory.Services.CreateScope())
            {
                var db = read.ServiceProvider.GetRequiredService<KanbadaDbContext>();
                var store = read.ServiceProvider.GetRequiredService<WorkspaceStore>();
                var metadata = await store.Read(workspace, user, cardIds: [], sharedNotificationsOnly: true);
                Assert.Empty(metadata.State["tasks"]!.AsArray());
                Assert.Empty(db.ChangeTracker.Entries<CardEntity>());
                var page = await read.ServiceProvider.GetRequiredService<CardQueries>().Page(workspace, user, new CardQuery(Project: "my-activities"), 40, null);
                var json = JsonSerializer.SerializeToNode(page, JsonSerializerOptions.Web)!;
                Assert.Equal(1_000_000, json["total"]!.GetValue<int>());
                Assert.Equal(40, json["items"]!.AsArray().Count);
                Assert.Equal(40, db.ChangeTracker.Entries<CardEntity>().Count());
                Assert.True(json.ToJsonString().Length < 32_000);
            }
            var last = await Body(await client.GetAsync("/api/workspaces/studio/cards/KB-SCALE-1000000"));
            Assert.Equal("Scale card 1000000", last["title"]!.ToString());
            await SavePartial(client, initial, new
            {
                changes = Array.Empty<object>(),
                removed = Array.Empty<string>(),
                upserts = new[] { new CardEdit("KB-SCALE-1000000", [new StateChange("replace", "/tasks/0/title", JsonValue.Create("Still bounded"))]) }
            });
            var summary = await Body(await client.GetAsync("/api/workspaces/studio/card-summary?project=my-activities"));
            Assert.Equal(1_000_000, summary["counts"]!["total"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task NotificationPagesPreservePrivateNoticesAcrossUnrelatedSaves()
    {
        var (client, _) = await User();
        using (client)
        {
            await SeedPages(client);
            var metadata = await Body(await client.GetAsync("/api/workspaces/studio?metadataOnly=true"));
            Assert.Empty(metadata["notifications"]!.AsArray());
            Assert.Equal(63, metadata["notificationCount"]!.GetValue<int>());
            var page = await Body(await client.GetAsync("/api/workspaces/studio/notification-feed"));
            Assert.Equal(40, page["items"]!.AsArray().Count);
            var remainder = await Body(await client.GetAsync("/api/workspaces/studio/notification-feed?after=" + Uri.EscapeDataString(page["nextCursor"]!.GetValue<string>())));
            Assert.Equal(23, remainder["items"]!.AsArray().Count);
            await SavePartial(client, metadata, new
            {
                changes = Array.Empty<object>(),
                removed = Array.Empty<string>(),
                upserts = new[] { new CardEdit("KB-PAGE-0124", [new StateChange("replace", "/tasks/0/title", JsonValue.Create("New title"))]) }
            });
            metadata = await Body(await client.GetAsync("/api/workspaces/studio?metadataOnly=true"));
            Assert.Equal(63, metadata["notificationCount"]!.GetValue<int>());
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/workspaces/studio/notification-feed");
            request.Headers.TryAddWithoutValidation("If-Match", metadata["version"]!.ToString());
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
            Assert.Equal(0, (await Body(await client.GetAsync("/api/workspaces/studio/notification-feed")))["total"]!.GetValue<int>());
        }
    }
}
