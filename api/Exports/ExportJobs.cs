using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kanbada.Api;

public sealed class ExportJobs(KanbadaDbContext db, CardQueries cards, TimeProvider clock)
{
    public static object Summary(ExportJobEntity job, DateTimeOffset now) => new
    {
        job.Id,
        job.Kind,
        job.Name,
        job.Locale,
        status = job.ExpiresAt <= now ? "expired" : job.Status,
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.ExpiresAt,
        job.Processed,
        job.Total,
        job.Bytes,
        job.SnapshotVersion,
        job.Error
    };

    public IQueryable<ExportJobEntity> Visible(Guid workspace, Guid user) =>
        db.Exports.AsNoTracking().Where(e => e.WorkspaceId == workspace && e.UserId == user
            && db.Members.Any(m => m.WorkspaceId == workspace && m.UserId == user));

    public async Task<ExportJobEntity> Find(Guid workspace, Guid user, Guid id, CancellationToken ct) =>
        await Visible(workspace, user).SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new ApiError(404, "Export not found or access denied.");

    public async Task<ExportJobEntity> Create(Guid workspace, Guid user, ExportRequest request, CancellationToken ct)
    {
        await cards.Authorize(workspace, user);
        if (request.Id == Guid.Empty || request.Kind is not ("project-json" or "dashboard-pdf" or "workspace-json")
            || request.Locale is not ("en-US" or "pt-PT"))
            throw new ApiError(400, "Invalid export request.");
        var query = request.Query ?? new CardQuery();
        if (request.Kind == "project-json")
        {
            if (string.IsNullOrEmpty(query.Project)) throw new ApiError(400, "Choose a project to export.");
            query = new CardQuery(Project: query.Project);
        }
        else if (request.Kind == "workspace-json") query = new CardQuery();
        else query = query with { Today = query.Today ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) };
        if (query.Completion is not (null or "Open" or "Completed" or "Overdue")
            || query.Priority is not (null or "Low" or "Medium" or "High") || query.From > query.To)
            throw new ApiError(400, "Invalid export filters.");
        var json = JsonSerializer.Serialize(query);
        if (json.Length > 8000) throw new ApiError(400, "Export filters are too long.");
        ExportJobEntity Match(ExportJobEntity existing)
        {
            if (existing.WorkspaceId != workspace || existing.UserId != user || existing.Kind != request.Kind
                || existing.Query != json || existing.Locale != request.Locale)
                throw new ApiError(409, "This export request ID is already in use.");
            return existing;
        }
        var existing = await db.Exports.AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.Id, ct);
        if (existing is not null) return Match(existing);
        var name = await db.Workspaces.Where(w => w.Id == workspace).Select(w => w.Name).SingleAsync(ct);
        if (query.Project is not null)
            name = await db.Projects.Where(p => p.WorkspaceId == workspace && p.Id == query.Project).Select(p => p.Name)
                .SingleOrDefaultAsync(ct) ?? throw new ApiError(404, "Project not found.");
        if (query.Mine == true) name = request.Locale == "pt-PT" ? "As minhas tarefas" : "My tasks";
        var job = new ExportJobEntity
        {
            Id = request.Id,
            WorkspaceId = workspace,
            UserId = user,
            Kind = request.Kind,
            Query = json,
            Locale = request.Locale,
            Name = name,
            CreatedAt = clock.GetUtcNow()
        };
        db.Exports.Add(job);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_exports" })
        {
            db.Entry(job).State = EntityState.Detached;
            return Match(await db.Exports.AsNoTracking().SingleAsync(e => e.Id == request.Id, ct));
        }
        return job;
    }

    private sealed record Cursor(DateTimeOffset At, Guid Id);
    public async Task<object> List(Guid workspace, Guid user, string? after, CancellationToken ct)
    {
        await cards.Authorize(workspace, user);
        var query = Visible(workspace, user);
        if (after is not null)
        {
            Cursor cursor;
            try { cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(after)) ?? throw new JsonException(); }
            catch (Exception e) when (e is FormatException or JsonException) { throw new ApiError(400, "Invalid export cursor."); }
            query = query.Where(e => e.CreatedAt < cursor.At || (e.CreatedAt == cursor.At && e.Id.CompareTo(cursor.Id) < 0));
        }
        var rows = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(21).ToListAsync(ct);
        var page = rows.Take(20).ToArray();
        var last = page.LastOrDefault();
        var nextCursor = rows.Count > 20 && last is not null
            ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(last.CreatedAt, last.Id))) : null;
        return new { items = page.Select(e => Summary(e, clock.GetUtcNow())), nextCursor };
    }
}
