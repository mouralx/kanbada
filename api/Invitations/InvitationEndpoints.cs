using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Kanbada.Api;

public static class InvitationEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api = api.MapGroup("").WithTags("Invitations");
        api.MapGet("/invitations/{token}", async (string token, KanbadaDbContext db) =>
            await (from member in db.Members
                   join workspace in db.Workspaces on member.WorkspaceId equals workspace.Id
                   where member.InviteToken == token && member.UserId == null
                   select new { workspace = workspace.Name, email = member.Email }).SingleOrDefaultAsync()
            ?? throw new ApiError(404, "Invitation unavailable."));
        api.MapPost("/invitations/{token}/accept", async (string token, HttpContext ctx, KanbadaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var user = Auth.User(ctx);
            var member = await db.Members.SingleOrDefaultAsync(x => x.InviteToken == token && x.UserId == null)
                ?? throw new ApiError(404, "Invitation unavailable.");
            if (!member.Email.Equals(ctx.User.FindFirstValue(ClaimTypes.Email), StringComparison.OrdinalIgnoreCase))
                throw new ApiError(403, "Sign in with the invited email address.");
            if (await db.Members.AnyAsync(x => x.WorkspaceId == member.WorkspaceId && x.UserId == user))
                throw new ApiError(409, "You are already a member.");
            var workspace = await db.Workspaces.SingleAsync(x => x.Id == member.WorkspaceId);
            member.UserId = user;
            member.Photo = await db.Users.Where(x => x.Id == user).Select(x => x.Photo).SingleAsync();
            member.InviteToken = null;
            workspace.Version++;
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { workspaceId = workspace.Id });
        });
    }
}
