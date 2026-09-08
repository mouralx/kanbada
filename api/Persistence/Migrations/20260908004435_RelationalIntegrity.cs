using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelationalIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cards_swimlanes_workspace_id_swimlane_id",
                table: "cards");

            migrationBuilder.DropIndex(
                name: "IX_swimlanes_workspace_id_project_id",
                table: "swimlanes");

            migrationBuilder.DropIndex(
                name: "IX_cards_workspace_id_project_id",
                table: "cards");

            migrationBuilder.DropIndex(
                name: "IX_cards_workspace_id_swimlane_id",
                table: "cards");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_swimlanes_workspace_id_project_id_id",
                table: "swimlanes",
                columns: new[] { "workspace_id", "project_id", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "users_normalized_email",
                table: "users",
                sql: "email = lower(email)");

            migrationBuilder.AddCheckConstraint(
                name: "members_normalized_email",
                table: "members",
                sql: "email = lower(email)");

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_project_id_swimlane_id",
                table: "cards",
                columns: new[] { "workspace_id", "project_id", "swimlane_id" });

            migrationBuilder.AddCheckConstraint(
                name: "cards_priority",
                table: "cards",
                sql: "priority IN ('Low','Medium','High')");

            migrationBuilder.AddForeignKey(
                name: "FK_cards_swimlanes_workspace_id_project_id_swimlane_id",
                table: "cards",
                columns: new[] { "workspace_id", "project_id", "swimlane_id" },
                principalTable: "swimlanes",
                principalColumns: new[] { "workspace_id", "project_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cards_swimlanes_workspace_id_project_id_swimlane_id",
                table: "cards");

            migrationBuilder.DropCheckConstraint(
                name: "users_normalized_email",
                table: "users");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_swimlanes_workspace_id_project_id_id",
                table: "swimlanes");

            migrationBuilder.DropCheckConstraint(
                name: "members_normalized_email",
                table: "members");

            migrationBuilder.DropIndex(
                name: "IX_cards_workspace_id_project_id_swimlane_id",
                table: "cards");

            migrationBuilder.DropCheckConstraint(
                name: "cards_priority",
                table: "cards");

            migrationBuilder.CreateIndex(
                name: "IX_swimlanes_workspace_id_project_id",
                table: "swimlanes",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_project_id",
                table: "cards",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_swimlane_id",
                table: "cards",
                columns: new[] { "workspace_id", "swimlane_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_cards_swimlanes_workspace_id_swimlane_id",
                table: "cards",
                columns: new[] { "workspace_id", "swimlane_id" },
                principalTable: "swimlanes",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
