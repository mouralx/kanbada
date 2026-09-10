using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceTwoFactorForAllAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "two_factor_required",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "two_factor_required",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
