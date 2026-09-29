using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GitHubProjectsSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "github_approved_hosts",
                columns: table => new
                {
                    authority = table.Column<string>(type: "text", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_approved_hosts", x => x.authority);
                });

            migrationBuilder.CreateTable(
                name: "github_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: false),
                    owner = table.Column<string>(type: "text", nullable: false),
                    owner_type = table.Column<string>(type: "text", nullable: false),
                    project_number = table.Column<int>(type: "integer", nullable: false),
                    remote_project_id = table.Column<string>(type: "text", nullable: false),
                    repository = table.Column<string>(type: "text", nullable: false),
                    repository_id = table.Column<string>(type: "text", nullable: false),
                    protected_token = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    status_field_id = table.Column<string>(type: "text", nullable: false),
                    priority_field_id = table.Column<string>(type: "text", nullable: false),
                    due_field_id = table.Column<string>(type: "text", nullable: false),
                    sync_assignees = table.Column<bool>(type: "boolean", nullable: false),
                    sync_labels = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_github_connections", x => x.id);
                    table.ForeignKey(
                        name: "FK_github_connections_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "github_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    item_id = table.Column<string>(type: "text", nullable: true),
                    content_id = table.Column<string>(type: "text", nullable: true),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    display_key = table.Column<string>(type: "text", nullable: true),
                    origin = table.Column<string>(type: "text", nullable: false),
                    kanbada_hash = table.Column<string>(type: "text", nullable: true),
                    git_hub_hash = table.Column<string>(type: "text", nullable: true),
                    creation_pending = table.Column<bool>(type: "boolean", nullable: false),
                    initialized = table.Column<bool>(type: "boolean", nullable: false),
                    write_pending = table.Column<bool>(type: "boolean", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    seen_run_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_github_links_github_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "github_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "github_mappings",
                columns: table => new
                {
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    git_hub_value = table.Column<string>(type: "text", nullable: false),
                    kanbada_value = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_mappings", x => new { x.connection_id, x.kind, x.git_hub_value });
                    table.ForeignKey(
                        name: "FK_github_mappings_github_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "github_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_github_connections_enabled_next_run_at",
                table: "github_connections",
                columns: new[] { "enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "IX_github_connections_workspace_id_base_url_remote_project_id",
                table: "github_connections",
                columns: new[] { "workspace_id", "base_url", "remote_project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_connections_workspace_id_project_id",
                table: "github_connections",
                columns: new[] { "workspace_id", "project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_links_connection_id_card_id",
                table: "github_links",
                columns: new[] { "connection_id", "card_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_links_connection_id_content_id",
                table: "github_links",
                columns: new[] { "connection_id", "content_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_links_connection_id_item_id",
                table: "github_links",
                columns: new[] { "connection_id", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_approved_hosts");

            migrationBuilder.DropTable(
                name: "github_links");

            migrationBuilder.DropTable(
                name: "github_mappings");

            migrationBuilder.DropTable(
                name: "github_connections");
        }
    }
}
