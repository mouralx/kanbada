using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Kanbada.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;

namespace Kanbada.Api;

public static class ServiceRegistration
{
    public static Dictionary<string, bool> AddKanbada(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Limits.MaxRequestBodySize = 32 * 1024 * 1024;
            o.AddServerHeader = false;
        });
        var trustedProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            if (trustedProxies.Length == 0) return;
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var address in trustedProxies) options.KnownProxies.Add(IPAddress.Parse(address));
            options.ForwardLimit = 1;
        });
        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 26 * 1024 * 1024);
        var connection = builder.Configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres.");
        builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
        builder.Services.AddDbContext<KanbadaDbContext>((services, options) => options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>()));
        builder.Services.AddScoped<WorkspaceMapper>();
        builder.Services.AddScoped<WorkspaceMetrics>();
        builder.Services.AddScoped<WorkspaceResolver>();
        builder.Services.AddScoped<DatabaseMigrator>();
        builder.Services.AddScoped<WorkspaceStore>();
        builder.Services.AddScoped<CardService>();
        builder.Services.AddScoped<Auth>();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddScoped<SessionEvents>();
        builder.Services.AddAuthorization();
        builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");
        builder.Services.AddOptions<ApiOptions>().Bind(builder.Configuration).ValidateDataAnnotations().Validate(o => Uri.TryCreate(o.PortalOrigin, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Host is "localhost" or "127.0.0.1"), "PortalOrigin must use HTTPS outside localhost.").ValidateOnStart();
        builder.Services.AddOpenApi(ApiDocumentation.Configure);
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".data-protection")));
        var providers = new Dictionary<string, bool>
        {
            ["google"] = !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientId"]) && !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientSecret"]),
            ["microsoft"] = !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Microsoft:ClientId"]) && !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Microsoft:ClientSecret"])
        };
        var authBuilder = builder.Services.AddAuthentication("session").AddCookie("session", o =>
        {
            o.Cookie.Name = "kanbada_session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = false;
            o.EventsType = typeof(SessionEvents);
        }).AddCookie("external", o =>
        {
            o.Cookie.Name = "kanbada_external";
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
        });
        if (providers["google"])
            authBuilder.AddGoogle("google", o =>
            {
                o.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
                o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
                o.SignInScheme = "external";
                o.CallbackPath = "/api/auth/google/callback";
                o.Events.OnRemoteFailure = c =>
                {
                    c.Response.Redirect("/?auth_error=failed");
                    c.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        if (providers["microsoft"])
            authBuilder.AddMicrosoftAccount("microsoft", o =>
            {
                o.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
                o.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
                o.SignInScheme = "external";
                o.CallbackPath = "/api/auth/microsoft/callback";
                o.Events.OnRemoteFailure = c =>
                {
                    c.Response.Redirect("/?auth_error=failed");
                    c.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return providers;
    }
}
