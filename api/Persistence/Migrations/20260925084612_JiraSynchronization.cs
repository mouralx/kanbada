using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JiraSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jira_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: false),
                    edition = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    protected_token = table.Column<string>(type: "text", nullable: false),
                    jql = table.Column<string>(type: "text", nullable: false),
                    jira_project_key = table.Column<string>(type: "text", nullable: false),
                    issue_type_id = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    cron = table.Column<string>(type: "text", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_synced_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_connections", x => x.id);
                    table.ForeignKey(
                        name: "FK_jira_connections_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jira_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    jira_issue_id = table.Column<string>(type: "text", nullable: true),
                    jira_key = table.Column<string>(type: "text", nullable: true),
                    origin = table.Column<string>(type: "text", nullable: false),
                    kanbada_hash = table.Column<string>(type: "text", nullable: true),
                    jira_hash = table.Column<string>(type: "text", nullable: true),
                    creation_pending = table.Column<bool>(type: "boolean", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_jira_links_jira_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "jira_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jira_mappings",
                columns: table => new
                {
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    kanbada_value = table.Column<string>(type: "text", nullable: false),
                    jira_value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_mappings", x => new { x.connection_id, x.kind, x.kanbada_value });
                    table.ForeignKey(
                        name: "FK_jira_mappings_jira_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "jira_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jira_connections_enabled_next_run_at",
                table: "jira_connections",
                columns: new[] { "enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "IX_jira_connections_workspace_id_project_id",
                table: "jira_connections",
                columns: new[] { "workspace_id", "project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jira_links_connection_id_card_id",
                table: "jira_links",
                columns: new[] { "connection_id", "card_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jira_links_connection_id_jira_issue_id",
                table: "jira_links",
                columns: new[] { "connection_id", "jira_issue_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jira_links");

            migrationBuilder.DropTable(
                name: "jira_mappings");

            migrationBuilder.DropTable(
                name: "jira_connections");
        }
    }
}
