using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kanbada.Api;

public sealed class ExportProcessor(IServiceScopeFactory scopes, NpgsqlDataSource source, TimeProvider clock, ILogger<ExportProcessor> logger)
{
    public async Task Run(Guid id, CancellationToken ct)
    {
        await using var lease = await source.OpenConnectionAsync(ct);
        await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@id, 0))", lease);
        acquire.Parameters.AddWithValue("id", "export:" + id);
        if (await acquire.ExecuteScalarAsync(ct) is not true) return;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            var job = await db.Exports.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
            if (job is null || job.Status is not ("queued" or "running")) return;
            if (job.Kind is not ("project-xlsx" or "workspace-xlsx" or "dashboard-pdf"))
                throw new ApiError(410, "This export format is no longer available. Request a new export.");
            if (job.Attempts >= 3)
            {
                await Fail(id, "Export interrupted repeatedly. Request a new export.", ct);
                return;
            }
            await db.ExportChunks.Where(c => c.ExportId == id).ExecuteDeleteAsync(ct);
            var started = clock.GetUtcNow();
            await db.Exports.Where(e => e.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, "running").SetProperty(e => e.Attempts, job.Attempts + 1)
                .SetProperty(e => e.StartedAt, started).SetProperty(e => e.Error, (string?)null)
                .SetProperty(e => e.Processed, 0).SetProperty(e => e.Total, (int?)null)
                .SetProperty(e => e.Bytes, 0).SetProperty(e => e.SnapshotVersion, (long?)null), ct);
            var query = JsonSerializer.Deserialize<CardQuery>(job.Query) ?? throw new InvalidOperationException("Missing export query.");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var file = new FileStream(Path.Combine(Path.GetTempPath(), $"kanbada-export-{id}-{Guid.NewGuid():N}.tmp"), options);
            await using (var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct))
            {
                var store = scope.ServiceProvider.GetRequiredService<WorkspaceStore>();
                var metadata = await store.Read(job.WorkspaceId, job.UserId, cardIds: [], sharedNotificationsOnly: true);
                await Progress(id, 0, null, metadata.Version, ct);
                if (query.Project is not null && !WorkspaceJson.Items(metadata.State, "projects").Any(p => WorkspaceJson.Text(p, "id") == query.Project))
                    throw new ApiError(404, "The project is no longer available.");
                var cards = scope.ServiceProvider.GetRequiredService<CardQueries>();
                if (job.Kind == "dashboard-pdf")
                {
                    var summary = JsonSerializer.SerializeToNode(await cards.Summary(job.WorkspaceId, job.UserId, query), JsonSerializerOptions.Web)!.AsObject();
                    var total = summary["counts"]!["total"]!.GetValue<int>();
                    await Progress(id, 0, total, metadata.Version, ct);
                    ct.ThrowIfCancellationRequested();
                    ExportPdf.Render(file, job, query, metadata.State, summary, started);
                    await Progress(id, total, total, metadata.Version, ct);
                }
                else
                {
                    using var workbook = new ExportWorkbook();
                    workbook.Metadata(metadata.State, job.Kind == "project-xlsx" ? query.Project : null);
                    if (job.Kind == "workspace-xlsx")
                    {
                        await foreach (var notice in db.Notifications.AsNoTracking()
                            .Where(n => n.WorkspaceId == job.WorkspaceId && (n.RecipientId == null || n.RecipientId == job.UserId))
                            .OrderBy(n => n.Position).ThenBy(n => n.Id).AsAsyncEnumerable().WithCancellation(ct))
                        {
                            workbook.Row("Notifications", JsonValue.Create(notice.Id), JsonValue.Create(notice.Message), JsonValue.Create(notice.At), JsonValue.Create(notice.CardId));
                        }
                    }
                    string? cursor = null;
                    var processed = 0;
                    do
                    {
                        ct.ThrowIfCancellationRequested();
                        db.ChangeTracker.Clear();
                        var page = JsonSerializer.SerializeToNode(await cards.Page(job.WorkspaceId, job.UserId, query, 100, cursor), JsonSerializerOptions.Web)!;
                        foreach (var card in page["items"]!.AsArray())
                        {
                            workbook.Card(card!);
                            processed++;
                        }
                        await Progress(id, processed, page["total"]!.GetValue<int>(), metadata.Version, ct);
                        cursor = page["nextCursor"]?.GetValue<string>();
                    } while (cursor is not null);
                    await workbook.Save(file, ct);
                }
                await snapshot.CommitAsync(ct);
            }
            await file.FlushAsync(ct);
            file.Position = 0;
            await Persist(id, file, ct);
            // Membership can be revoked while a long-running snapshot is being generated.
            if (!await db.Members.AnyAsync(m => m.WorkspaceId == job.WorkspaceId && m.UserId == job.UserId, ct))
                throw new ApiError(403, "Workspace access was removed during export.");
            var completed = clock.GetUtcNow();
            await db.Exports.Where(e => e.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, "completed").SetProperty(e => e.Bytes, file.Length)
                .SetProperty(e => e.CompletedAt, completed).SetProperty(e => e.ExpiresAt, completed.AddDays(7)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
            await db.Exports.Where(e => e.Id == id && e.Status == "running").ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, "queued"), CancellationToken.None);
            logger.LogInformation("Export {ExportId} paused during shutdown; it will restart.", id);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Export {ExportId} failed.", id);
            await Fail(id, error is ApiError ? error.Message : "Export generation failed. Request a new export or contact an administrator.", CancellationToken.None);
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@id, 0))", lease);
            release.Parameters.AddWithValue("id", "export:" + id);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task Progress(Guid id, int processed, int? total, long version, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        await db.Exports.Where(e => e.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Processed, processed).SetProperty(e => e.Total, total)
            .SetProperty(e => e.SnapshotVersion, version), ct);
    }

    private async Task Persist(Guid id, Stream file, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        var buffer = new byte[1024 * 1024];
        var position = 0;
        int count;
        while ((count = await file.ReadAsync(buffer, ct)) > 0)
        {
            db.ExportChunks.Add(new ExportChunkEntity { ExportId = id, Position = position++, Bytes = buffer.AsSpan(0, count).ToArray() });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task Fail(Guid id, string message, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.ExportChunks.Where(c => c.ExportId == id).ExecuteDeleteAsync(ct);
        var now = clock.GetUtcNow();
        await db.Exports.Where(e => e.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, "failed").SetProperty(e => e.Error, message)
            .SetProperty(e => e.CompletedAt, now).SetProperty(e => e.ExpiresAt, now.AddDays(7))
            .SetProperty(e => e.Bytes, 0), ct);
        await tx.CommitAsync(ct);
    }

    public async Task Expire(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbadaDbContext>();
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ids = await db.Exports.Where(e => e.ExpiresAt <= now && e.Status != "expired")
            .OrderBy(e => e.ExpiresAt).Select(e => e.Id).Take(100).ToArrayAsync(ct);
        await db.ExportChunks.Where(c => ids.Contains(c.ExportId)).ExecuteDeleteAsync(ct);
        await db.Exports.Where(e => ids.Contains(e.Id)).ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, "expired").SetProperty(e => e.Bytes, 0), ct);
        await tx.CommitAsync(ct);
    }
}
