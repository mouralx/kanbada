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
                             join user in db.Users on session.UserId equals user.Id
                             where session.Id == sid && session.ExpiresAt > DateTimeOffset.UtcNow
                             select user).AsNoTracking().SingleOrDefaultAsync();
        if (profile is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync("session");
            return;
        }

        context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, profile.Id.ToString()), new Claim(ClaimTypes.Email, profile.Email), new Claim(ClaimTypes.Name, profile.Name), new Claim("sid", sid) }, "session")));
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
