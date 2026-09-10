using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace Kanbada.Api;

public sealed class SessionEvents(KanbadaDbContext db) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var sid = context.Principal?.FindFirstValue("sid");
        if (sid is null)
        {
            context.RejectPrincipal();
            return;
        }

        var profile = await (from session in db.Sessions
                             join account in db.Users on session.UserId equals account.Id
                             where session.Id == sid && session.ExpiresAt > DateTimeOffset.UtcNow
                             select new { User = account, session.TwoFactorVerified }).AsNoTracking().SingleOrDefaultAsync();
        if (profile is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync("session");
            return;
        }

        var user = profile.User;
        var pending = user.TwoFactorSecret == null || !profile.TwoFactorVerified;
        context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Name, user.Name), new Claim("sid", sid), new Claim("avatar_pending", user.PhotoRequired && user.Photo == null ? "true" : "false"), new Claim("two_factor_pending", pending ? "true" : "false"), new Claim("two_factor_setup", user.TwoFactorSecret == null ? "true" : "false") }, "session")));
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
