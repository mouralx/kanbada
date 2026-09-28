using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Kanbada.Api;

public static class AssignmentNotifications
{
    public static async Task CreateForNewAssignments(KanbadaDbContext db, Guid workspace, CancellationToken ct)
    {
        var added = db.ChangeTracker.Entries<CardAssigneeEntity>()
            .Where(e => e.State == EntityState.Added && e.Entity.WorkspaceId == workspace).Select(e => e.Entity).ToList();
        if (added.Count == 0) return;
        var position = await db.Notifications.Where(n => n.WorkspaceId == workspace).MinAsync(n => (int?)n.Position, ct) ?? 0;
        foreach (var assignment in added)
        {
            var member = db.Members.Local.SingleOrDefault(m => m.WorkspaceId == workspace && m.Email == assignment.MemberEmail)
                ?? await db.Members.SingleAsync(m => m.WorkspaceId == workspace && m.Email == assignment.MemberEmail, ct);
            if (member.UserId is not Guid recipient) continue;
            var card = db.Cards.Local.Single(c => c.WorkspaceId == workspace && c.Id == assignment.CardId);
            db.Add(new NotificationEntity
            {
                WorkspaceId = workspace,
                Id = "assignment-" + Guid.NewGuid().ToString("N"),
                RecipientId = recipient,
                CardId = card.Id,
                Message = card.Title,
                At = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Position = --position
            });
        }
    }
}
