using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

public sealed class WorkspaceMappingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task LargeWorkspaceMappingPreservesHistoryOrderWithoutRepeatedGraphScans()
    {
        using var client = fixture.Client(); // Initialize only this fixture's isolated schema.
        var scans = 0;
        var options = new DbContextOptionsBuilder<KanbadaDbContext>().UseNpgsql(fixture.Connection)
            .LogTo(_ => scans++, [CoreEventId.DetectChangesStarting]).Options;
        await using var db = new KanbadaDbContext(options);
        var owner = new UserEntity { Id = Guid.NewGuid(), Email = "mapping@example.test", Name = "Mapper Owner" };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var mapper = new WorkspaceMapper(db);
        var store = new WorkspaceStore(db, mapper);
        var workspace = await store.Create(owner.Id, "Mapping performance");
        for (var n = 0; n < 1000; n++)
        {
            var card = $"KB-MAP-{n}";
            db.Cards.Add(new CardEntity { WorkspaceId = workspace, Id = card, ProjectId = "my-activities", StatusId = "backlog", Title = card, Priority = "Medium", Position = n });
            for (var h = 0; h < 4; h++)
            {
                db.HistoryEntries.Add(new HistoryEntryEntity { WorkspaceId = workspace, CardId = card, Id = "history-" + h, Actor = owner.Name, At = DateTimeOffset.UtcNow.AddHours(-h), Position = h });
                db.HistoryChanges.Add(new HistoryChangeEntity { WorkspaceId = workspace, CardId = card, HistoryId = "history-" + h, Text = $"{card} change {h}", Position = 0 });
            }
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        scans = 0;
        var state = (await store.Read(workspace, owner.Id)).State;
        Assert.True(db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.InRange(scans, 0, 2); // Reading must not repeatedly scan the tracked graph as card count increases.
        var cards = WorkspaceJson.Items(state, "tasks");
        Assert.Equal(1000, cards.Count);
        Assert.Equal("KB-MAP-999", cards[999]!["id"]!.ToString());
        Assert.Equal("KB-MAP-999 change 3", cards[999]!["history"]![3]!["changes"]![0]!.ToString());
        cards[500]!["title"] = "Persisted after reading";
        var saved = await store.Save(workspace, owner.Id, state, state["version"]!.GetValue<long>());
        Assert.True(db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal("Persisted after reading", saved["tasks"]![500]!["title"]!.ToString());
        Assert.Equal(5, saved["tasks"]![500]!["history"]!.AsArray().Count);
        db.ChangeTracker.Clear();
        Assert.Equal("Persisted after reading", (await db.Cards.SingleAsync(x => x.WorkspaceId == workspace && x.Id == "KB-MAP-500")).Title);
    }
}
