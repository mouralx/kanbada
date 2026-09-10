using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticatorTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "two_factor_failed_attempts",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "two_factor_last_step",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "two_factor_locked_until",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "two_factor_pending_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_factor_pending_secret",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_factor_secret",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_codes",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_codes", x => new { x.user_id, x.hash });
                    table.ForeignKey(
                        name: "FK_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_codes");

            migrationBuilder.DropColumn(
                name: "two_factor_failed_attempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_last_step",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_locked_until",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_pending_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_pending_secret",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_secret",
                table: "users");
        }
    }
}
