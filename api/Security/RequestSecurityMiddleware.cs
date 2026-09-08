using Microsoft.Extensions.Options;

namespace Kanbada.Api;
/// <summary>Rejects cross-origin cookie mutations. Keep the portal and API on the same public origin.</summary>
public sealed class RequestSecurityMiddleware(RequestDelegate next, IOptions<ApiOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        context.Response.Headers["Cache-Control"] = "no-store";
        if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            var origin = context.Request.Headers.Origin.ToString();
            var allowed = options.Value.PortalOrigin;
            if (context.Request.Headers["X-Kanbada-Request"] != "1" || (!string.IsNullOrEmpty(origin) && origin != allowed && origin != $"{context.Request.Scheme}://{context.Request.Host}"))
                throw new ApiError(403, "Request origin rejected.");
        }

        await next(context);
    }
}
