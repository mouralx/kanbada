using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfSharp.Pdf.IO;
using Xunit;
using static ApiTests;

public sealed class ExportTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Path = "/api/workspaces/studio/exports";
    private ServiceProvider Worker(TimeProvider? clock = null, IInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fixture.Factory.Services.GetRequiredService<NpgsqlDataSource>());
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddDbContext<KanbadaDbContext>((provider, options) =>
        {
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>());
            if (interceptor is not null) options.AddInterceptors(interceptor);
        });
        services.AddScoped<WorkspaceMapper>();
        services.AddScoped<WorkspaceStore>();
        services.AddScoped<CardQueries>();
        services.AddSingleton<ExportProcessor>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<Guid> Queue(HttpClient client, string kind = "workspace-json", CardQuery? query = null, string locale = "en-US")
    {
        using var response = await client.PostAsJsonAsync(Path, new ExportRequest(Guid.NewGuid(), kind, query, locale));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("queued", body["status"]!.ToString());
        var id = Guid.Parse(body["id"]!.ToString());
        Assert.EndsWith("/exports/" + id, response.Headers.Location!.ToString());
        return id;
    }

    [Fact]
    public async Task QueueIsPrivateIdempotentAndDoesNotRenderInline()
    {
        var (client, _, _) = await CreateUser(fixture);
        var (other, otherEmail, _) = await CreateUser(fixture);
        using (client)
        using (other)
        {
            var state = await Body(await client.GetAsync("/api/workspaces/studio?metadataOnly=true"));
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            var owner = Guid.Parse(state["workspace"]!["ownerId"]!.ToString());
            var workspace = await scope.ServiceProvider.GetRequiredService<WorkspaceResolver>().WorkspaceId("studio", owner);
            var teammate = await db.Users.SingleAsync(u => u.Email == otherEmail);
            db.Members.Add(new MemberEntity { WorkspaceId = workspace, UserId = teammate.Id, Email = teammate.Email, Name = teammate.Name, Initials = "TM", Color = "#123456", Position = 1 });
            await db.SaveChangesAsync();
            var request = new ExportRequest(Guid.NewGuid(), "workspace-json");
            var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.PostAsJsonAsync(Path, request)));
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                    var job = await Body(response);
                    Assert.Equal(request.Id.ToString(), job["id"]!.ToString());
                    Assert.Equal("queued", job["status"]!.ToString());
                }
            }
            Assert.Equal(1, await db.Exports.CountAsync(e => e.Id == request.Id));
            Assert.False(await db.ExportChunks.AnyAsync(c => c.ExportId == request.Id));
            Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"{Path}/{request.Id}/download")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Path, request with { Kind = "dashboard-pdf" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, request with { Id = Guid.NewGuid(), Kind = "invalid" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new ExportRequest(Guid.NewGuid(), "project-json"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new ExportRequest(Guid.NewGuid(), "dashboard-pdf", new CardQuery(Completion: "invalid")))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Path + "?after=invalid")).StatusCode);
            var otherPath = $"/api/workspaces/{workspace}/exports";
            Assert.Empty((await Body(await other.GetAsync(otherPath)))["items"]!.AsArray());
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{otherPath}/{request.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{otherPath}/{request.Id}/download")).StatusCode);
            await using var worker = Worker();
            await worker.GetRequiredService<ExportProcessor>().Run(request.Id, default);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{otherPath}/{request.Id}/download")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Path}/{request.Id}/download")).StatusCode);
            await db.Members.Where(m => m.WorkspaceId == workspace && m.UserId == owner).ExecuteDeleteAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Path}/{request.Id}/download")).StatusCode);
        }
    }

    [Fact]
    public async Task JsonExportsIncludeAllPagesAssociationsAndVisibleNotificationsInChunks()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            var state = await PaginationTests.SeedPages(client);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            var workspace = await scope.ServiceProvider.GetRequiredService<WorkspaceResolver>().WorkspaceId("studio", Guid.Parse(state["workspace"]!["ownerId"]!.ToString()));
            await db.Cards.Where(c => c.WorkspaceId == workspace).ExecuteUpdateAsync(s => s.SetProperty(c => c.Description, new string('x', 12000)));
            state = await Body(await client.GetAsync("/api/workspaces/studio"));
            var id = await Queue(client);
            var project = await Queue(client, "project-json", new CardQuery(Project: "my-activities", Search: "not-present"));
            await using var worker = Worker();
            var processor = worker.GetRequiredService<ExportProcessor>();
            await processor.Run(id, default);
            await processor.Run(project, default);
            foreach (var jobId in new[] { id, project })
            {
                var status = await Body(await client.GetAsync($"{Path}/{jobId}"));
                Assert.Equal("completed", status["status"]!.ToString());
                Assert.Equal(125, status["processed"]!.GetValue<int>());
                Assert.Equal(125, status["total"]!.GetValue<int>());
                Assert.Equal(state["version"]!.ToString(), status["snapshotVersion"]!.ToString());
                var completed = DateTimeOffset.Parse(status["completedAt"]!.ToString());
                Assert.Equal(completed.AddDays(7), DateTimeOffset.Parse(status["expiresAt"]!.ToString()));
                using var download = await client.GetAsync($"{Path}/{jobId}/download");
                var bytes = await download.Content.ReadAsByteArrayAsync();
                Assert.Equal(status["bytes"]!.GetValue<long>(), bytes.LongLength);
                Assert.Equal(bytes.LongLength, download.Content.Headers.ContentLength);
                Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
                Assert.True(download.Headers.CacheControl!.NoStore);
                var data = JsonNode.Parse(bytes)!;
                Assert.True(JsonNode.DeepEquals(state["tasks"], data["tasks"]));
                if (jobId == id)
                    foreach (var (key, value) in state) Assert.True(JsonNode.DeepEquals(value, data[key]), key);
                else
                {
                    Assert.Equal(2, data.AsObject().Count);
                    Assert.Equal("my-activities", data["project"]!["id"]!.ToString());
                }
                Assert.True(await db.ExportChunks.CountAsync(c => c.ExportId == jobId) > 1);
                Assert.True(await db.ExportChunks.Where(c => c.ExportId == jobId).AllAsync(c => c.Bytes.Length <= 1024 * 1024));
            }
        }
    }

    [Fact]
    public async Task GenerationUsesOneSnapshotAndClearsCardsBetweenPages()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            var state = await PaginationTests.SeedPages(client);
            var id = await Queue(client, "project-json", new CardQuery(Project: "my-activities"));
            var interceptor = new PageObserver(async () =>
            {
                using var scope = fixture.Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
                var job = await db.Exports.AsNoTracking().SingleAsync(e => e.Id == id);
                Assert.Equal("running", job.Status);
                Assert.Equal(100, job.Processed);
                await db.Cards.Where(c => c.WorkspaceId == job.WorkspaceId && c.Id == "KB-PAGE-0124").ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, "Changed after snapshot"));
                await db.Workspaces.Where(w => w.Id == job.WorkspaceId).ExecuteUpdateAsync(s => s.SetProperty(w => w.Version, w => w.Version + 1));
            });
            await using var worker = Worker(interceptor: interceptor);
            await worker.GetRequiredService<ExportProcessor>().Run(id, default);
            var status = await Body(await client.GetAsync($"{Path}/{id}"));
            Assert.Equal("completed", status["status"]!.ToString());
            Assert.Equal(2, interceptor.Pages);
            Assert.Equal(0, interceptor.MaxTrackedCardsBeforePage);
            var data = await Body(await client.GetAsync($"{Path}/{id}/download"));
            Assert.True(JsonNode.DeepEquals(state["tasks"], data["tasks"]));
            Assert.Equal("Changed after snapshot", (await Body(await client.GetAsync("/api/workspaces/studio/cards/KB-PAGE-0124")))["title"]!.ToString());
        }
    }

    [Fact]
    public async Task PdfUsesFilteredAggregatesAndSupportsPortuguese()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            await PaginationTests.SeedPages(client);
            var id = await Queue(client, "dashboard-pdf", new CardQuery(Project: "my-activities", Bucket: "Paging", Swimlane: "lane", Today: new DateOnly(2026, 9, 29)), "pt-PT");
            var observer = new PageObserver(() => Task.CompletedTask);
            await using var worker = Worker(interceptor: observer);
            await worker.GetRequiredService<ExportProcessor>().Run(id, default);
            var status = await Body(await client.GetAsync($"{Path}/{id}"));
            Assert.Equal("completed", status["status"]!.ToString());
            Assert.Equal(63, status["processed"]!.GetValue<int>());
            Assert.Equal(0, observer.Pages);
            var response = await client.GetAsync($"{Path}/{id}/download");
            Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 8));
            using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            Assert.Contains("Relatório do painel", pdf.Info.Title);
            Assert.InRange(pdf.PageCount, 2, 6);
        }
    }

    [Fact]
    public async Task RunningJobsRecoverWithExclusiveClaimsAndBoundedAttempts()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            var id = await Queue(client);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            await db.Exports.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, "running").SetProperty(e => e.Attempts, 1));
            db.ExportChunks.Add(new ExportChunkEntity { ExportId = id, Position = 0, Bytes = Encoding.UTF8.GetBytes("incomplete") });
            await db.SaveChangesAsync();
            await using var lease = await fixture.Factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
            await using var claim = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@id, 0))", lease);
            claim.Parameters.AddWithValue("id", "export:" + id);
            await claim.ExecuteNonQueryAsync();
            await using var worker = Worker();
            var processor = worker.GetRequiredService<ExportProcessor>();
            await processor.Run(id, default);
            Assert.Equal(1, await db.Exports.Where(e => e.Id == id).Select(e => e.Attempts).SingleAsync());
            claim.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@id, 0))";
            await claim.ExecuteNonQueryAsync();
            await Task.WhenAll(processor.Run(id, default), processor.Run(id, default));
            Assert.Equal(2, await db.Exports.Where(e => e.Id == id).Select(e => e.Attempts).SingleAsync());
            Assert.Equal("completed", (await Body(await client.GetAsync($"{Path}/{id}")))["status"]!.ToString());
            Assert.Empty((await Body(await client.GetAsync($"{Path}/{id}/download")))["tasks"]!.AsArray());
            var failed = await Queue(client);
            await db.Exports.Where(e => e.Id == failed).ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, "running").SetProperty(e => e.Attempts, 3));
            await processor.Run(failed, default);
            var status = await Body(await client.GetAsync($"{Path}/{failed}"));
            Assert.Equal("failed", status["status"]!.ToString());
            Assert.Contains("interrupted", status["error"]!.ToString());
            Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"{Path}/{failed}/download")).StatusCode);
        }
    }

    [Fact]
    public async Task ExpiredFilesAreImmediatelyDeniedAndCleanedWhileHistoryRemainsPaged()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < 23; i++) ids.Add(await Queue(client));
            var id = ids[0];
            await using (var oldWorker = Worker(new FixedClock(DateTimeOffset.UtcNow.AddDays(-8))))
                await oldWorker.GetRequiredService<ExportProcessor>().Run(id, default);
            Assert.Equal("expired", (await Body(await client.GetAsync($"{Path}/{id}")))["status"]!.ToString());
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"{Path}/{id}/download")).StatusCode);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            Assert.True(await db.ExportChunks.AnyAsync(c => c.ExportId == id));
            await using var worker = Worker();
            await worker.GetRequiredService<ExportProcessor>().Expire(default);
            Assert.False(await db.ExportChunks.AnyAsync(c => c.ExportId == id));
            Assert.Equal("expired", await db.Exports.Where(e => e.Id == id).Select(e => e.Status).SingleAsync());
            var first = await Body(await client.GetAsync(Path));
            Assert.Equal(20, first["items"]!.AsArray().Count);
            var second = await Body(await client.GetAsync(Path + "?after=" + Uri.EscapeDataString(first["nextCursor"]!.ToString())));
            Assert.Equal(3, second["items"]!.AsArray().Count);
            Assert.Null(second["nextCursor"]);
            Assert.Equal(23, first["items"]!.AsArray().Concat(second["items"]!.AsArray()).Select(e => e!["id"]!.ToString()).Distinct().Count());
        }
    }

    [Fact]
    public async Task RevokedAccessFailsGenerationWithoutPublishingAFile()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            var id = await Queue(client);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            var job = await db.Exports.AsNoTracking().SingleAsync(e => e.Id == id);
            await db.Members.Where(m => m.WorkspaceId == job.WorkspaceId && m.UserId == job.UserId).ExecuteDeleteAsync();
            await using var worker = Worker();
            await worker.GetRequiredService<ExportProcessor>().Run(id, default);
            Assert.Equal("failed", await db.Exports.Where(e => e.Id == id).Select(e => e.Status).SingleAsync());
            Assert.False(await db.ExportChunks.AnyAsync(c => c.ExportId == id));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PageObserver(Func<Task> beforeSecondPage) : DbCommandInterceptor
    {
        public int Pages { get; private set; }
        public int MaxTrackedCardsBeforePage { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM cards") && command.CommandText.Contains("LIMIT")
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is 101))
            {
                Pages++;
                MaxTrackedCardsBeforePage = Math.Max(MaxTrackedCardsBeforePage, eventData.Context!.ChangeTracker.Entries<CardEntity>().Count());
                if (Pages == 2) await beforeSecondPage();
            }
            return result;
        }
    }
}
