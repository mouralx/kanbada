using Kanbada.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations;

[DbContext(typeof(KanbadaDbContext))]
[Migration("20260929120000_SeedDefaultPlatformBranding")]
public sealed class SeedDefaultPlatformBranding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO platform_branding (
                id, version, name, logo, "primary", accent, light_background, dark_background,
                default_theme, light_surface, dark_surface, light_text, dark_text,
                light_border, dark_border, sidebar_background, sidebar_text,
                font_family, font_scale, corner_radius, show_name, collapsed_logo
            ) VALUES (
                1, 0, 'kanbada', NULL, '#334153', '#aac2e1', '#f8f9fb', '#17191c',
                'system', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                'default', 100, 8, TRUE, NULL
            )
            ON CONFLICT (id) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the singleton row: it may contain branding saved after this migration.
    }
}
