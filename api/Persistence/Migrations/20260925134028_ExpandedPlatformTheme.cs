using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandedPlatformTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "corner_radius",
                table: "platform_branding",
                type: "integer",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<string>(
                name: "dark_border",
                table: "platform_branding",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dark_surface",
                table: "platform_branding",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dark_text",
                table: "platform_branding",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "font_family",
                table: "platform_branding",
                type: "text",
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<int>(
                name: "font_scale",
                table: "platform_branding",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<string>(
                name: "light_border",
                table: "platform_branding",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "light_surface",
                table: "platform_branding",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "light_text",
                table: "platform_branding",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sidebar_background",
                table: "platform_branding",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sidebar_text",
                table: "platform_branding",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "corner_radius",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "dark_border",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "dark_surface",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "dark_text",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "font_family",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "font_scale",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "light_border",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "light_surface",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "light_text",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "sidebar_background",
                table: "platform_branding");

            migrationBuilder.DropColumn(
                name: "sidebar_text",
                table: "platform_branding");
        }
    }
}
