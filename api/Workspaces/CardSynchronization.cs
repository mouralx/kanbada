using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class CardSynchronization
{
    public static Task<List<string>> Labels(KanbadaDbContext db, CardEntity card, CancellationToken ct) =>
        (from link in db.CardLabels
         join label in db.Labels on new { link.WorkspaceId, Id = link.LabelId } equals new { label.WorkspaceId, label.Id }
         where link.WorkspaceId == card.WorkspaceId && link.CardId == card.Id
         select label.Name).ToListAsync(ct);

    public static async Task ApplyLabels(KanbadaDbContext db, CardEntity card, IEnumerable<string> labels, string source, CancellationToken ct)
    {
        var definitions = await db.Labels.Where(l => l.WorkspaceId == card.WorkspaceId).ToListAsync(ct);
        var existing = await db.CardLabels.Where(l => l.WorkspaceId == card.WorkspaceId && l.CardId == card.Id).ToListAsync(ct);
        var wanted = new HashSet<string>();
        foreach (var name in labels.Select(name => name.Trim()))
        {
            var label = definitions.SingleOrDefault(l => string.Equals(l.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (label is null)
            {
                label = new LabelEntity { WorkspaceId = card.WorkspaceId, Id = source + "-" + Guid.NewGuid().ToString("N"), Name = name, Color = "#879eb9", Position = definitions.Count };
                definitions.Add(label);
                db.Add(label);
            }
            if (wanted.Add(label.Id) && !existing.Any(l => l.LabelId == label.Id))
                db.Add(new CardLabelEntity { WorkspaceId = card.WorkspaceId, CardId = card.Id, LabelId = label.Id, Position = wanted.Count - 1 });
        }
        db.RemoveRange(existing.Where(l => !wanted.Contains(l.LabelId)));
    }

    public static async Task SaveWorkspace(KanbadaDbContext db, WorkspaceEntity workspace, CardEntity card, string actor, string change, CancellationToken ct)
    {
        await AssignmentNotifications.CreateForNewAssignments(db, workspace.Id, ct);
        workspace.Version++;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.HistoryEntries.Where(h => h.WorkspaceId == card.WorkspaceId && h.CardId == card.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Position, h => h.Position + 1), ct);
        var historyId = Guid.NewGuid().ToString("N");
        db.Add(new HistoryEntryEntity { WorkspaceId = card.WorkspaceId, CardId = card.Id, Id = historyId, Actor = actor, At = DateTimeOffset.UtcNow, Position = 0 });
        db.Add(new HistoryChangeEntity { WorkspaceId = card.WorkspaceId, CardId = card.Id, HistoryId = historyId, Position = 0, Text = change });
        await db.SaveChangesAsync(ct);
    }

    public static string Winner(string source, string direction, string origin, string localHash, string remoteHash, string? previousLocal, string? previousRemote)
    {
        if (localHash == remoteHash) return "none";
        if (direction == source + "-to-kanbada") return source;
        if (direction == "kanbada-to-" + source) return "kanbada";
        var localChanged = localHash != previousLocal;
        var remoteChanged = remoteHash != previousRemote;
        if (localChanged && remoteChanged) return origin;
        return localChanged ? "kanbada" : remoteChanged ? source : "none";
    }
}
