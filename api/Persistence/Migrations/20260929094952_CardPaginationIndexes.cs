using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CardPaginationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_position_id",
                table: "cards",
                columns: new[] { "workspace_id", "position", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_project_id_bucket_id_position_id",
                table: "cards",
                columns: new[] { "workspace_id", "project_id", "bucket_id", "position", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_project_id_status_id_position_id",
                table: "cards",
                columns: new[] { "workspace_id", "project_id", "status_id", "position", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cards_workspace_id_position_id",
                table: "cards");

            migrationBuilder.DropIndex(
                name: "IX_cards_workspace_id_project_id_bucket_id_position_id",
                table: "cards");

            migrationBuilder.DropIndex(
                name: "IX_cards_workspace_id_project_id_status_id_position_id",
                table: "cards");
        }
    }
}
