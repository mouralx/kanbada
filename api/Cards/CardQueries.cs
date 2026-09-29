using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public sealed record CardQuery(
    string? Project = null, bool? Mine = null, bool? Active = null, string? Search = null, string? Priority = null,
    string? Person = null, string? Bucket = null, string? Swimlane = null, string? Status = null, string? Completion = null,
    DateOnly? From = null, DateOnly? To = null, DateOnly? Today = null, bool? Unassigned = null);

public sealed record CardCounts(int Total, int Completed, int Open, int Overdue, int HighPriority, int Unassigned);

internal sealed record PageCursor(int Position, string Id, long Version, string Scope, int Total)
{
    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
    public static PageCursor Read(string value, string scope, long version)
    {
        PageCursor cursor;
        try { cursor = JsonSerializer.Deserialize<PageCursor>(Convert.FromBase64String(value)) ?? throw new JsonException(); }
        catch (Exception e) when (e is FormatException or JsonException) { throw new ApiError(400, "Invalid page cursor."); }
        if (string.IsNullOrEmpty(cursor.Id) || cursor.Total < 0) throw new ApiError(400, "Invalid page cursor.");
        if (cursor.Scope != scope) throw new ApiError(400, "The cursor belongs to a different query.");
        if (cursor.Version != version) throw new ApiError(409, "This workspace changed. Reload before continuing.");
        return cursor;
    }
}

public sealed class CardQueries(KanbadaDbContext db, WorkspaceStore store)
{
    public IQueryable<CardEntity> Filter(Guid workspace, Guid user, CardQuery q)
    {
        var cards = db.Cards.AsNoTracking().Where(c => c.WorkspaceId == workspace);
        if (q.Project is not null) cards = cards.Where(c => c.ProjectId == q.Project);
        if (q.Active == true || q.Mine == true)
            cards = cards.Where(c => db.Projects.Any(p => p.WorkspaceId == workspace && p.Id == c.ProjectId && !p.Archived));
        if (q.Mine == true)
            cards = cards.Where(c => db.CardAssignees.Any(a => a.WorkspaceId == workspace && a.CardId == c.Id
                && db.Members.Any(m => m.WorkspaceId == workspace && m.Email == a.MemberEmail && m.UserId == user)));
        if (q.Person is not null)
            cards = cards.Where(c => db.CardAssignees.Any(a => a.WorkspaceId == workspace && a.CardId == c.Id
                && db.Members.Any(m => m.WorkspaceId == workspace && m.Email == a.MemberEmail && m.Name == q.Person)));
        if (q.Search is { Length: > 0 })
        {
            var search = q.Search.ToLower();
            var labelNames = from a in db.CardLabels
                             join l in db.Labels on new { a.WorkspaceId, Id = a.LabelId } equals new { l.WorkspaceId, l.Id }
                             where a.WorkspaceId == workspace
                             group new { a.Position, l.Name } by a.CardId into g
                             select new { Id = g.Key, Names = string.Join(" ", g.OrderBy(x => x.Position).Select(x => x.Name)) };
            cards = from card in cards
                    join names in labelNames on card.Id equals names.Id into namesForCard
                    from names in namesForCard.DefaultIfEmpty()
                    where (card.Title + " " + (names.Names ?? "") + " " + card.Id).ToLower().Contains(search)
                    select card;
        }
        if (q.Priority is not null) cards = cards.Where(c => c.Priority == q.Priority);
        if (q.Status is not null) cards = cards.Where(c => db.Statuses.Any(s => s.WorkspaceId == workspace && s.Id == c.StatusId && s.Name == q.Status));
        if (q.Bucket is not null)
            cards = q.Bucket == "" ? cards.Where(c => c.BucketId == null)
                : cards.Where(c => db.Buckets.Any(b => b.WorkspaceId == workspace && b.Id == c.BucketId && b.Name == q.Bucket));
        if (q.Swimlane is not null) cards = cards.Where(c => c.SwimlaneId == (q.Swimlane == "" ? null : q.Swimlane));
        if (q.Completion is not null)
        {
            if (q.Completion is not ("Completed" or "Open" or "Overdue")) throw new ApiError(400, "Invalid completion filter.");
            cards = cards.Where(c => db.Statuses.Any(s => s.WorkspaceId == workspace && s.Id == c.StatusId && s.Complete == (q.Completion == "Completed")));
            if (q.Completion == "Overdue") cards = cards.Where(c => c.Due < (q.Today ?? DateOnly.FromDateTime(DateTime.UtcNow)));
        }
        if (q.From is not null) cards = cards.Where(c => c.Due >= q.From);
        if (q.To is not null) cards = cards.Where(c => c.Due <= q.To);
        if (q.Unassigned == true) cards = cards.Where(c => !db.CardAssignees.Any(a => a.WorkspaceId == workspace && a.CardId == c.Id));
        return cards;
    }

    public async Task<long> Authorize(Guid workspace, Guid user) =>
        await db.Workspaces.Where(w => w.Id == workspace && db.Members.Any(m => m.WorkspaceId == workspace && m.UserId == user))
            .Select(w => (long?)w.Version).SingleOrDefaultAsync() ?? throw new ApiError(404, "Workspace not found or access denied.");

    public async Task<object> Page(Guid workspace, Guid user, CardQuery query, int? limit, string? after)
    {
        var size = limit ?? 40;
        if (size is < 1 or > 100) throw new ApiError(400, "Page size must be between 1 and 100.");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
        var version = await Authorize(workspace, user);
        var scope = workspace + ":" + user + ":" + JsonSerializer.Serialize(query);
        var cards = Filter(workspace, user, query);
        var cursor = after is null ? null : PageCursor.Read(after, scope, version);
        var total = cursor?.Total ?? await cards.CountAsync();
        if (cursor is not null)
        {
            cards = cards.Where(c => c.Position > cursor.Position || (c.Position == cursor.Position && c.Id.CompareTo(cursor.Id) > 0));
        }
        var rows = await cards.OrderBy(c => c.Position).ThenBy(c => c.Id).Select(c => new { c.Id, c.Position }).Take(size + 1).ToListAsync();
        var ids = rows.Take(size).Select(c => c.Id).ToArray();
        var state = (await store.Read(workspace, user, cardIds: ids, sharedNotificationsOnly: true)).State;
        var byId = WorkspaceJson.Items(state, "tasks").ToDictionary(c => WorkspaceJson.Text(c, "id"));
        var items = ids.Select(id => byId[id]!.DeepClone()).ToArray();
        var last = rows.Take(size).LastOrDefault();
        var nextCursor = rows.Count > size && last is not null
            ? new PageCursor(last.Position, last.Id, version, scope, total).Encode() : null;
        if (tx is not null) await tx.CommitAsync();
        return new { items, nextCursor, total, version };
    }

    public async Task<object> Summary(Guid workspace, Guid user, CardQuery query)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
        var version = await Authorize(workspace, user);
        var today = query.Today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var cards = Filter(workspace, user, query);
        var rows = from card in cards
                   join status in db.Statuses on new { card.WorkspaceId, Id = card.StatusId } equals new { status.WorkspaceId, status.Id }
                   select new { card, status.Complete };
        var counts = await rows.GroupBy(x => 1).Select(g => new CardCounts(g.Count(), g.Count(x => x.Complete),
            g.Count(x => !x.Complete), g.Count(x => !x.Complete && x.card.Due < today),
            g.Count(x => !x.Complete && x.card.Priority == "High"),
            g.Count(x => !x.Complete && !db.CardAssignees.Any(a => a.WorkspaceId == workspace && a.CardId == x.card.Id))))
            .SingleOrDefaultAsync() ?? new CardCounts(0, 0, 0, 0, 0, 0);
        var groups = await rows.GroupBy(x => new { project = x.card.ProjectId, status = x.card.StatusId, bucket = x.card.BucketId, swimlane = x.card.SwimlaneId })
            .Select(g => new
            {
                g.Key.project,
                g.Key.status,
                g.Key.bucket,
                g.Key.swimlane,
                total = g.Count(),
                completed = g.Count(x => x.Complete),
                overdue = g.Count(x => !x.Complete && x.card.Due < today)
            }).ToListAsync();
        var due = await rows.Where(x => x.card.Due >= today && x.card.Due < today.AddDays(7)).GroupBy(x => x.card.Due)
            .Select(g => new { date = g.Key, total = g.Count(), completed = g.Count(x => x.Complete) }).ToListAsync();
        var workload = await (from a in db.CardAssignees
                              join x in rows on new { a.WorkspaceId, Id = a.CardId } equals new { x.card.WorkspaceId, x.card.Id }
                              where !x.Complete
                              group a by a.MemberEmail into g
                              select new { email = g.Key, total = g.Count() }).ToListAsync();
        var checklist = await db.ChecklistItems.Where(c => c.WorkspaceId == workspace && cards.Any(x => x.Id == c.CardId))
            .GroupBy(x => 1).Select(g => new { total = g.Count(), completed = g.Count(x => x.Done) }).SingleOrDefaultAsync();
        var usedLabels = await db.CardLabels.Where(l => l.WorkspaceId == workspace && cards.Any(c => c.Id == l.CardId))
            .Select(l => l.LabelId).Distinct().ToListAsync();
        var activity = await (from entry in db.HistoryEntries
                              join card in cards on new { entry.WorkspaceId, Id = entry.CardId } equals new { card.WorkspaceId, card.Id }
                              orderby entry.At descending, entry.Id
                              select new
                              {
                                  cardId = card.Id,
                                  title = card.Title,
                                  entry.Id,
                                  entry.At,
                                  entry.Actor,
                                  change = db.HistoryChanges.Where(c => c.WorkspaceId == workspace && c.CardId == card.Id && c.HistoryId == entry.Id)
                                      .OrderBy(c => c.Position).Select(c => c.Text).FirstOrDefault()
                              }).Take(5).ToListAsync();
        var myOpen = await Filter(workspace, user, query with { Mine = true, Completion = "Open" }).CountAsync();
        if (tx is not null) await tx.CommitAsync();
        return new { version, counts, groups, due, workload, checklist, usedLabels, activity, myOpen };
    }
}
