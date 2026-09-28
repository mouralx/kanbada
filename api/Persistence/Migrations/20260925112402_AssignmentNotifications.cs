using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "card_id",
                table: "notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recipient_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_id",
                table: "notifications",
                column: "recipient_id");

            migrationBuilder.AddCheckConstraint(
                name: "notifications_assignment_recipient",
                table: "notifications",
                sql: "(recipient_id IS NULL) = (card_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_users_recipient_id",
                table: "notifications",
                column: "recipient_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM notifications WHERE recipient_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'Dismiss or archive private assignment notifications before downgrading; they cannot become shared notifications.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_users_recipient_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_recipient_id",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "notifications_assignment_recipient",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "card_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "recipient_id",
                table: "notifications");
        }
    }
}
