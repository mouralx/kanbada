using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Kanbada.Api;

public sealed class Auth(KanbadaDbContext db, WorkspaceStore workspaces)
{
    public static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    public static Guid User(HttpContext ctx) => Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ApiError(401, "Please sign in.");
    public async Task Session(HttpContext ctx, Guid user, bool twoFactorVerified = false)
    {
        var previous = ctx.User.FindFirstValue("sid");
        await db.Sessions.Where(x => x.Id == previous || x.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync();
        var sid = Hash(Token());
        db.Sessions.Add(new SessionEntity { Id = sid, UserId = user, TwoFactorVerified = twoFactorVerified, ExpiresAt = DateTimeOffset.UtcNow.AddHours(8) });
        await db.SaveChangesAsync();
        var profile = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim(ClaimTypes.Name, profile.Name), new Claim(ClaimTypes.Email, profile.Email), new Claim("sid", sid) }, "session"));
        await ctx.SignInAsync("session", principal, new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
    }

    public async Task<Guid> Register(Credentials input)
    {
        var email = (input.Email ?? "").Trim().ToLowerInvariant();
        var name = input.Name?.Trim() ?? "";
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || name.Length is < 1 or > 120 || (input.Password?.Length ?? 0) is < 12 or > 200)
            throw new ApiError(400, "Enter a valid email, name, and a password of 12 to 200 characters.");
        var id = Guid.NewGuid();
        var hash = new PasswordHasher<string>().HashPassword(id.ToString(), input.Password!);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            db.Users.Add(new UserEntity { Id = id, Email = email, Name = name, PasswordHash = hash, PhotoRequired = true });
            await db.SaveChangesAsync();
            await workspaces.Create(id, "My Workspace", true);
            await tx.CommitAsync();
            return id;
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new ApiError(409, "An account with this email already exists.");
        }
    }

    public async Task<Guid> Login(Credentials input)
    {
        if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 200)
            throw new ApiError(401, "Email or password is incorrect.");
        var email = (input.Email ?? "").Trim().ToLowerInvariant();
        var profile = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email.ToLower() == email && x.PasswordHash != null)
            ?? throw new ApiError(401, "Email or password is incorrect.");
        var id = profile.Id;
        var result = new PasswordHasher<string>().VerifyHashedPassword(id.ToString(), profile.PasswordHash!, input.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new ApiError(401, "Email or password is incorrect.");
        return id;
    }

    public async Task Logout(HttpContext ctx)
    {
        var sid = ctx.User.FindFirstValue("sid");
        if (sid != null)
        {
            await db.Sessions.Where(x => x.Id == sid).ExecuteDeleteAsync();
        }

        await ctx.SignOutAsync("session");
    }

    public async Task<Guid> External(string provider, ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ApiError(400, "Provider identity missing.");
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? throw new ApiError(400, "This provider did not return an email address.");
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? email;
        var existing = await db.Identities.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == provider && x.Subject == subject);
        if (existing is not null) return existing.UserId;
        await using var tx = await db.Database.BeginTransactionAsync();
        var id = Guid.NewGuid();
        db.Users.Add(new UserEntity { Id = id, Email = email.ToLowerInvariant(), Name = name, PhotoRequired = true });
        db.Identities.Add(new IdentityEntity { Provider = provider, Subject = subject, UserId = id });
        await db.SaveChangesAsync();
        await workspaces.Create(id, "My Workspace", true);
        await tx.CommitAsync();
        return id;
    }
}
