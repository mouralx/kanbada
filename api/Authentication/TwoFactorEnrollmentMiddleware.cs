namespace Kanbada.Api;

/// <summary>Enrollment/second-factor sessions cannot access application data or other auth flows.</summary>
public sealed class TwoFactorEnrollmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim("two_factor_pending", "true") && context.Request.Path.StartsWithSegments("/api"))
        {
            var path = context.Request.Path.Value!.TrimEnd('/').ToLowerInvariant();
            var allowed = context.Request.Method == "GET" && path is "/api/auth/session" or "/api/auth/two-factor"
                || context.Request.Method == "POST" && path is "/api/auth/logout" or "/api/auth/two-factor/setup" or "/api/auth/two-factor/confirm" or "/api/auth/two-factor/verify";
            if (!allowed) throw new ApiError(403, "Complete two-factor authentication before accessing your workspace.");
        }
        await next(context);
    }
}
