using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace Kanbada.Api;

public static class AuthenticationEndpoints
{
    public static void Map(WebApplication app, Dictionary<string, bool> providers)
    {
        var api = app.MapGroup("").WithTags("Authentication");
        api.MapGet("/api/auth/session", (HttpContext ctx) => Results.Ok(new { user = ctx.User.Identity?.IsAuthenticated == true ? new { id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), name = ctx.User.FindFirstValue(ClaimTypes.Name), email = ctx.User.FindFirstValue(ClaimTypes.Email), provider = "email" } : null, providers, twoFactorSetupRequired = ctx.User.HasClaim("two_factor_pending", "true") && ctx.User.HasClaim("two_factor_setup", "true"), twoFactorVerificationRequired = ctx.User.HasClaim("two_factor_pending", "true") && !ctx.User.HasClaim("two_factor_setup", "true") }));
        api.MapPost("/api/auth/register", async (Credentials input, Auth auth, HttpContext ctx) =>
        {
            var user = await auth.Register(input);
            await auth.Session(ctx, user);
            return Results.Ok(new { id = user, twoFactorSetupRequired = true });
        }).RequireRateLimiting("auth");
        api.MapPost("/api/auth/login", async (Credentials input, Auth auth, TwoFactor twoFactor, HttpContext ctx) =>
        {
            var user = await auth.Login(input);
            if (!await twoFactor.Login(user, input.Code, () => auth.Session(ctx, user, twoFactorVerified: true)))
                return Results.Ok(new { twoFactorRequired = true });
            return Results.NoContent();
        }).RequireRateLimiting("auth");
        var security = api.MapGroup("/api/auth/two-factor").RequireAuthorization().RequireRateLimiting("auth");
        api.MapGet("/api/auth/two-factor", async (TwoFactor factor, HttpContext ctx) => Results.Ok(await factor.Status(Auth.User(ctx))))
            .RequireAuthorization().RequireRateLimiting("auth");
        security.MapPost("/setup", async (TwoFactorInput input, TwoFactor factor, HttpContext ctx) =>
            Results.Ok(await factor.Setup(Auth.User(ctx), input.Password)));
        security.MapPost("/confirm", async (TwoFactorInput input, TwoFactor factor, HttpContext ctx) =>
            Results.Ok(await factor.Confirm(Auth.User(ctx), input, ctx.User.FindFirstValue("sid"))));
        security.MapPost("/recovery-codes", async (TwoFactorInput input, TwoFactor factor, HttpContext ctx) =>
            Results.Ok(await factor.Regenerate(Auth.User(ctx), input, ctx.User.FindFirstValue("sid"))));
        security.MapPost("/verify", async (TwoFactorInput input, TwoFactor factor, HttpContext ctx) =>
        {
            await factor.VerifySession(Auth.User(ctx), input.Code, ctx.User.FindFirstValue("sid"));
            return Results.NoContent();
        });
        security.MapPost("/disable", async (TwoFactorInput input, TwoFactor factor, HttpContext ctx) =>
        {
            await factor.Disable(Auth.User(ctx), input, ctx.User.FindFirstValue("sid"));
            return Results.NoContent();
        });
        api.MapPost("/api/auth/logout", async (Auth auth, HttpContext ctx) =>
        {
            await auth.Logout(ctx);
            return Results.NoContent();
        });
        api.MapGet("/api/auth/{provider}/start", (string provider, string? returnUrl) =>
        {
            if (!providers.GetValueOrDefault(provider))
                throw new ApiError(503, "This sign-in provider has not been configured.");
            var safe = returnUrl?.StartsWith('/') == true && !returnUrl.StartsWith("//") && !returnUrl.Contains('\\') ? returnUrl : "/";
            return Results.Challenge(new AuthenticationProperties { RedirectUri = "/api/auth/complete?provider=" + provider + "&returnUrl=" + Uri.EscapeDataString(safe) }, new[] { provider });
        }).RequireRateLimiting("auth");
        api.MapGet("/api/auth/complete", async (string provider, string? returnUrl, HttpContext ctx, Auth auth) =>
        {
            if (!providers.GetValueOrDefault(provider))
                throw new ApiError(400, "Unknown provider.");
            var external = await ctx.AuthenticateAsync("external");
            if (!external.Succeeded || external.Principal is null)
                throw new ApiError(401, "Sign-in failed.");
            await auth.Session(ctx, await auth.External(provider, external.Principal));
            await ctx.SignOutAsync("external");
            return Results.Redirect(returnUrl?.StartsWith('/') == true && !returnUrl.StartsWith("//") && !returnUrl.Contains('\\') ? returnUrl : "/");
        });
    }
}
