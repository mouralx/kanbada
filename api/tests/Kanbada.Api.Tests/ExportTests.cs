using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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

    private static async Task<Guid> Queue(HttpClient client, string kind = "workspace-xlsx", CardQuery? query = null, string locale = "en-US")
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
            var request = new ExportRequest(Guid.NewGuid(), "workspace-xlsx");
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
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new ExportRequest(Guid.NewGuid(), "project-xlsx"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new ExportRequest(Guid.NewGuid(), "workspace-json"))).StatusCode);
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
    public async Task WorkbooksIncludeAllPagesAssociationsAndVisibleNotificationsInChunks()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            var state = await PaginationTests.SeedPages(client);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            var workspace = await scope.ServiceProvider.GetRequiredService<WorkspaceResolver>().WorkspaceId("studio", Guid.Parse(state["workspace"]!["ownerId"]!.ToString()));
            var cards = await db.Cards.Where(c => c.WorkspaceId == workspace).ToListAsync();
            foreach (var card in cards) card.Description = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12000));
            await db.SaveChangesAsync();
            state = await Body(await client.GetAsync("/api/workspaces/studio"));
            var id = await Queue(client);
            var project = await Queue(client, "project-xlsx", new CardQuery(Project: "my-activities", Search: "not-present"));
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
                Assert.Equal(ExportWorkbook.ContentType, download.Content.Headers.ContentType!.MediaType);
                Assert.EndsWith(".xlsx", download.Content.Headers.ContentDisposition!.FileNameStar!);
                var data = ReadWorkbook(bytes);
                Assert.Equal(125, data["Cards"].Count);
                for (var i = 0; i < 125; i++)
                    foreach (var (key, value) in data["Cards"][i])
                        Assert.Equal(state["tasks"]![i]![key]?.ToString() ?? "", value?.ToString() ?? "");
                Assert.Equal(125, data["Checklist"].Count);
                Assert.Equal(63, data["Assignees"].Count);
                Assert.Equal("Needle", Assert.Single(data["Card labels"])["label"]!.ToString());
                Assert.Equal(125, data["History"].Count);
                if (jobId == id)
                {
                    Assert.Equal(state["notifications"]!.AsArray().Count, data["Notifications"].Count);
                    Assert.Equal("studio", Assert.Single(data["Workspace"])["id"]!.ToString());
                    Assert.DoesNotContain("invitationToken", Assert.Single(data["Members"]).Select(p => p.Key));
                }
                else
                {
                    Assert.False(data.ContainsKey("Workspace"));
                    Assert.Equal("my-activities", Assert.Single(data["Projects"])["id"]!.ToString());
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
            var id = await Queue(client, "project-xlsx", new CardQuery(Project: "my-activities"));
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
            var data = ReadWorkbook(await client.GetByteArrayAsync($"{Path}/{id}/download"));
            Assert.Equal(state["tasks"]!.AsArray().Select(t => t!["title"]!.ToString()), data["Cards"].Select(t => t["title"]!.ToString()));
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
            Assert.Empty(ReadWorkbook(await client.GetByteArrayAsync($"{Path}/{id}/download"))["Cards"]);
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

    [Fact]
    public async Task WorkbookSplitsSheetsAndLongTextWithoutCreatingFormulas()
    {
        using var workbook = new ExportWorkbook(maxRowsPerSheet: 3);
        workbook.Table("Data", "id", "text", "done", "number");
        var text = new string('x', 32766) + "\U0001F600" + "ação\r\n" + new string('y', 100);
        workbook.Row("Data", JsonValue.Create("first"), JsonValue.Create("=HYPERLINK(\"https://example.test\")"), JsonValue.Create(true), JsonValue.Create(12));
        workbook.Row("Data", JsonValue.Create("second"), JsonValue.Create(text), JsonValue.Create(false), JsonValue.Create(13));
        workbook.Row("Data", JsonValue.Create("third"), JsonValue.Create("literal _x0000_ and \0"), null, null);
        using var stream = new WriteOnlyStream();
        await workbook.Save(stream, default);
        var data = ReadWorkbook(stream.ToArray());
        Assert.Equal(2, data["Data"].Count);
        Assert.Single(data["Data (2)"]);
        Assert.Equal(text, string.Concat(data["Long text"].Select(row => row["text"]!.ToString())));
        Assert.Equal("literal _x0000_ and \0", data["Data (2)"][0]["text"]!.ToString());
        Assert.True(data["Data"][0]["done"]!.GetValue<bool>());
        Assert.Equal(12, data["Data"][0]["number"]!.GetValue<int>());
    }

    [Fact]
    public async Task FormatMigrationExpiresJsonFilesAndRequeuesPendingWorkbooks()
    {
        var (client, _, _) = await CreateUser(fixture);
        using (client)
        {
            var completed = await Queue(client);
            var pending = await Queue(client, "project-xlsx", new CardQuery(Project: "my-activities"));
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            await db.Exports.Where(e => e.Id == completed).ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Kind, "workspace-json").SetProperty(e => e.Status, "completed").SetProperty(e => e.Bytes, 2));
            await db.Exports.Where(e => e.Id == pending).ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Kind, "project-json").SetProperty(e => e.Status, "running").SetProperty(e => e.Attempts, 1));
            db.ExportChunks.Add(new ExportChunkEntity { ExportId = completed, Position = 0, Bytes = Encoding.UTF8.GetBytes("{}") });
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"{Path}/{completed}/download")).StatusCode);
            foreach (var sql in new FormatMigration().Commands()) await db.Database.ExecuteSqlRawAsync(sql);
            Assert.False(await db.ExportChunks.AnyAsync(c => c.ExportId == completed));
            var old = await db.Exports.AsNoTracking().SingleAsync(e => e.Id == completed);
            Assert.Equal("workspace-xlsx", old.Kind);
            Assert.Equal("expired", old.Status);
            var queued = await db.Exports.AsNoTracking().SingleAsync(e => e.Id == pending);
            Assert.Equal("project-xlsx", queued.Kind);
            Assert.Equal("queued", queued.Status);
            Assert.Equal(0, queued.Attempts);
            await using var worker = Worker();
            await worker.GetRequiredService<ExportProcessor>().Run(pending, default);
            Assert.Empty(ReadWorkbook(await client.GetByteArrayAsync($"{Path}/{pending}/download"))["Cards"]);
        }
    }

    private sealed class FormatMigration : Kanbada.Api.Persistence.Migrations.XlsxExportFormats
    {
        public IEnumerable<string> Commands()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations.Cast<SqlOperation>().Select(operation => operation.Sql);
        }
    }

    private sealed class WriteOnlyStream : Stream
    {
        private readonly MemoryStream buffer = new();
        public byte[] ToArray() => buffer.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => buffer.Flush();
        public override void Write(byte[] bytes, int offset, int count) => buffer.Write(bytes, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => buffer.WriteAsync(bytes, cancellationToken);
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) buffer.Dispose(); base.Dispose(disposing); }
    }

    private static Dictionary<string, List<JsonObject>> ReadWorkbook(byte[] bytes)
    {
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(document).Take(10).Select(e => e.Description + " " + e.Path?.XPath));
        var result = new Dictionary<string, List<JsonObject>>();
        foreach (var sheet in document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>())
        {
            var part = (WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!);
            Assert.Empty(part.Worksheet.Descendants<CellFormula>());
            var rows = part.Worksheet.GetFirstChild<SheetData>()!.Elements<Row>().ToArray();
            var headers = rows[0].Elements<Cell>().Select(c => c.InnerText).ToArray();
            result[sheet.Name!] = rows.Skip(1).Select(row =>
            {
                var record = new JsonObject();
                var cells = row.Elements<Cell>().ToArray();
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = cells[i];
                    record[headers[i]] = cell.DataType?.Value == CellValues.Boolean ? JsonValue.Create(cell.CellValue!.Text == "1")
                        : cell.DataType?.Value == CellValues.Number ? JsonNode.Parse(cell.CellValue!.Text)
                        : JsonValue.Create(Regex.Replace(cell.InnerText, "_x([0-9A-Fa-f]{4})_", match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString()));
                }
                return record;
            }).ToList();
        }
        return result;
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
