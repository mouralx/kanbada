using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Kanbada.Api;

public sealed class PlatformBrandingEntity
{
    public int Id { get; set; } = 1;
    public long Version { get; set; }
    public string Name { get; set; } = "kanbada";
    public string? Logo { get; set; }
    public string? CollapsedLogo { get; set; }
    public bool ShowName { get; set; } = true;
    public string Primary { get; set; } = "#334153";
    public string Accent { get; set; } = "#aac2e1";
    public string LightBackground { get; set; } = "#f8f9fb";
    public string DarkBackground { get; set; } = "#17191c";
    public string DefaultTheme { get; set; } = "system";
    public string? LightSurface { get; set; }
    public string? DarkSurface { get; set; }
    public string? LightText { get; set; }
    public string? DarkText { get; set; }
    public string? LightBorder { get; set; }
    public string? DarkBorder { get; set; }
    public string? SidebarBackground { get; set; }
    public string? SidebarText { get; set; }
    public string FontFamily { get; set; } = "default";
    public int FontScale { get; set; } = 100;
    public int CornerRadius { get; set; } = 8;
}

public sealed record BrandingInput(long Version, string Name, string? Logo, string Primary,
    string Accent, string LightBackground, string DarkBackground, string DefaultTheme,
    string? LightSurface = null, string? DarkSurface = null, string? LightText = null,
    string? DarkText = null, string? LightBorder = null, string? DarkBorder = null,
    string? SidebarBackground = null, string? SidebarText = null,
    string FontFamily = "default", int FontScale = 100, int CornerRadius = 8, bool ShowName = true,
    string? CollapsedLogo = null);

public sealed class PlatformAdmins
{
    private HashSet<Guid> ids = [];

    public async Task Initialize(IConfiguration configuration, KanbadaDbContext db)
    {
        var emails = (configuration["Platform:AdminEmails"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant()).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(u => emails.Contains(u.Email)).Select(u => new { u.Id, u.Email }).ToListAsync();
        // Resolve existing identities at startup, never grant privileges to future signups.
        if (emails.Any(email => users.Count(u => u.Email == email) != 1))
            throw new InvalidOperationException("Each Platform:AdminEmails entry must match exactly one existing account. Register accounts before configuring platform administrators.");
        ids = users.Select(u => u.Id).ToHashSet();
    }

    public bool Contains(Guid user) => ids.Contains(user);
    public void Require(Guid user)
    {
        if (!Contains(user)) throw new ApiError(403, "Only a platform administrator can change platform branding.");
    }
}

public sealed class PlatformBranding(KanbadaDbContext db, PlatformAdmins admins)
{
    public async Task<PlatformBrandingEntity> Read(CancellationToken ct) =>
        await db.Set<PlatformBrandingEntity>().AsNoTracking().SingleOrDefaultAsync(ct) ?? new PlatformBrandingEntity();

    public async Task<PlatformBrandingEntity> Save(Guid user, BrandingInput input, CancellationToken ct)
    {
        admins.Require(user);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 60 || input.Name.Any(char.IsControl))
            throw new ApiError(400, "Platform name must contain 1 to 60 printable characters.");
        foreach (var color in new[] { input.Primary, input.Accent, input.LightBackground, input.DarkBackground })
            if (color is null || !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                throw new ApiError(400, "Colors must use six-digit hexadecimal values, for example #334153.");
        if (input.DefaultTheme is not ("light" or "dark" or "system"))
            throw new ApiError(400, "Select light, dark, or system as the default theme.");
        foreach (var color in new[] { input.LightSurface, input.DarkSurface, input.LightText, input.DarkText,
            input.LightBorder, input.DarkBorder, input.SidebarBackground, input.SidebarText })
            if (color is not null && !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                throw new ApiError(400, "Optional theme colors must be empty for automatic colors or use #RRGGBB.");
        if (input.FontFamily is not ("default" or "system" or "dm-sans" or "manrope")
            || input.FontScale is < 90 or > 120 || input.CornerRadius is < 0 or > 16)
            throw new ApiError(400, "Select a supported font, text size between 90% and 120%, and corner rounding between 0 and 16.");
        foreach (var logo in new[] { input.Logo, input.CollapsedLogo })
        {
            if (logo is null) continue;
            try { AccountAvatar.Validate(logo); }
            catch (ApiError) { throw new ApiError(400, "Upload a PNG, JPG, GIF, or WebP logo smaller than 2 MB."); }
        }
        var entity = await db.Set<PlatformBrandingEntity>().SingleOrDefaultAsync(ct);
        if ((entity?.Version ?? 0) != input.Version) throw new ApiError(409, "Platform branding changed. Reload before saving.");
        if (entity is null) { entity = new PlatformBrandingEntity(); db.Add(entity); }
        entity.Version++;
        entity.Name = input.Name.Trim();
        entity.Logo = input.Logo;
        entity.CollapsedLogo = input.CollapsedLogo;
        entity.ShowName = input.ShowName;
        entity.Primary = input.Primary.ToLowerInvariant();
        entity.Accent = input.Accent.ToLowerInvariant();
        entity.LightBackground = input.LightBackground.ToLowerInvariant();
        entity.DarkBackground = input.DarkBackground.ToLowerInvariant();
        entity.DefaultTheme = input.DefaultTheme;
        entity.LightSurface = input.LightSurface?.ToLowerInvariant();
        entity.DarkSurface = input.DarkSurface?.ToLowerInvariant();
        entity.LightText = input.LightText?.ToLowerInvariant();
        entity.DarkText = input.DarkText?.ToLowerInvariant();
        entity.LightBorder = input.LightBorder?.ToLowerInvariant();
        entity.DarkBorder = input.DarkBorder?.ToLowerInvariant();
        entity.SidebarBackground = input.SidebarBackground?.ToLowerInvariant();
        entity.SidebarText = input.SidebarText?.ToLowerInvariant();
        entity.FontFamily = input.FontFamily;
        entity.FontScale = input.FontScale;
        entity.CornerRadius = input.CornerRadius;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/platform/branding", async (PlatformBranding branding, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            var value = await branding.Read(ct);
            var etag = $"\"branding-{value.Version}\"";
            ctx.Response.Headers.ETag = etag;
            return ctx.Request.Headers.IfNoneMatch.ToString() == etag ? Results.StatusCode(304) : Results.Ok(value);
        }).WithTags("Platform branding").AllowAnonymous();
        app.MapGet("/api/platform/branding/access", (PlatformAdmins admins, HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { canManage = admins.Contains(Auth.User(ctx)) });
        }).WithTags("Platform branding").RequireAuthorization();
        app.MapPut("/api/platform/branding", async (BrandingInput input, PlatformBranding branding, HttpContext ctx, CancellationToken ct) =>
            Results.Ok(await branding.Save(Auth.User(ctx), input, ct))).WithTags("Platform branding").RequireAuthorization();
    }
}
