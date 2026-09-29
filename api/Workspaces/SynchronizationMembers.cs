using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class SynchronizationMembers
{
    public static async Task<string?> FindOrAdd(KanbadaDbContext db, Guid workspace, string? remoteEmail, string? displayName, CancellationToken ct)
    {
        var email = remoteEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || email.Any(char.IsControl) || !MailAddress.TryCreate(email, out var address)
            || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            return null;
        var existing = db.Members.Local.SingleOrDefault(m => m.WorkspaceId == workspace && m.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            ?? await db.Members.SingleOrDefaultAsync(m => m.WorkspaceId == workspace && m.Email.ToLower() == email, ct);
        if (existing is not null) return existing.Email;
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120 || name.Any(char.IsControl)) return null;
        var names = (await db.Members.Where(m => m.WorkspaceId == workspace).Select(m => m.Name).ToListAsync(ct))
            .Concat(db.Members.Local.Where(m => m.WorkspaceId == workspace).Select(m => m.Name))
            .Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uniqueName = name;
        for (var suffix = 2; names.Contains(uniqueName); suffix++)
        {
            var ending = $" ({suffix})";
            var length = Math.Min(name.Length, 120 - ending.Length);
            if (char.IsHighSurrogate(name[length - 1])) length--;
            uniqueName = name[..length] + ending;
        }
        var position = Math.Max(await db.Members.Where(m => m.WorkspaceId == workspace).MaxAsync(m => (int?)m.Position, ct) ?? -1,
            db.Members.Local.Where(m => m.WorkspaceId == workspace).Select(m => m.Position).DefaultIfEmpty(-1).Max()) + 1;
        db.Add(new MemberEntity
        {
            WorkspaceId = workspace,
            Email = email,
            Name = uniqueName,
            Position = position,
            Initials = string.Concat(uniqueName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => n.EnumerateRunes().First().ToString().ToUpperInvariant())),
            Color = "#879eb9",
            InviteToken = Auth.Token()
        });
        return email;
    }
}
