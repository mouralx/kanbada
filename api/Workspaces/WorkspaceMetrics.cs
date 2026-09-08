using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public sealed class WorkspaceMetrics(KanbadaDbContext db)
{
    public async Task<object> Calculate(Guid workspace, Guid user, DateOnly today, string? project = null, string? bucket = null, string? swimlane = null)
    {
        if (!await db.Members.AnyAsync(x => x.WorkspaceId == workspace && x.UserId == user))
            throw new ApiError(404, "Workspace not found or access denied.");
        var cards = from card in db.Cards
                    join definition in db.Projects on new { card.WorkspaceId, Id = card.ProjectId } equals new { definition.WorkspaceId, definition.Id }
                    join status in db.Statuses on new { card.WorkspaceId, Id = card.StatusId } equals new { status.WorkspaceId, status.Id }
                    where card.WorkspaceId == workspace && !definition.Archived
                        && (project == null || card.ProjectId == project)
                        && (bucket == null || db.Buckets.Any(b => b.WorkspaceId == workspace && b.Id == card.BucketId && b.Name == bucket))
                        && (swimlane == null || db.Swimlanes.Any(l => l.WorkspaceId == workspace && l.Id == card.SwimlaneId && l.Name == swimlane))
                    select new { card, status.Complete };
        var metrics = await cards.GroupBy(x => 1).Select(group => new Metrics(
            group.Count(), group.Count(x => x.Complete), group.Count(x => !x.Complete),
            group.Count(x => !x.Complete && x.card.Due != null && x.card.Due < today),
            group.Count(x => !x.Complete && x.card.Priority == "High"),
            group.Count(x => !x.Complete && !db.CardAssignees.Any(a => a.WorkspaceId == workspace && a.CardId == x.card.Id))
        )).SingleOrDefaultAsync();
        return metrics ?? new Metrics(0, 0, 0, 0, 0, 0);
    }

    private sealed record Metrics(int Total, int Completed, int Open, int Overdue, int HighPriority, int Unassigned);
}
