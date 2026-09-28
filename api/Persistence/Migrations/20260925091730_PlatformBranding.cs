using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_branding",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    logo = table.Column<string>(type: "text", nullable: true),
                    primary = table.Column<string>(type: "text", nullable: false),
                    accent = table.Column<string>(type: "text", nullable: false),
                    light_background = table.Column<string>(type: "text", nullable: false),
                    dark_background = table.Column<string>(type: "text", nullable: false),
                    default_theme = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_branding", x => x.id);
                    table.CheckConstraint("platform_branding_singleton", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_branding");
        }
    }
}
