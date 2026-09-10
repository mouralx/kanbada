using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequiredRegistrationAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "photo",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "photo_required",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "photo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "photo_required",
                table: "users");
        }
    }
}
