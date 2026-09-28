using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JiraFlexibleMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_jira_mappings",
                table: "jira_mappings");

            migrationBuilder.AddColumn<bool>(
                name: "is_default",
                table: "jira_mappings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "sync_assignees",
                table: "jira_connections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_jira_mappings",
                table: "jira_mappings",
                columns: new[] { "connection_id", "kind", "jira_value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM jira_mappings WHERE kind = 'assignee')
                       OR EXISTS (SELECT 1 FROM jira_mappings GROUP BY connection_id, kind, kanbada_value HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Remove assignee mappings and reduce statuses to one Jira value before downgrading.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropPrimaryKey(
                name: "PK_jira_mappings",
                table: "jira_mappings");

            migrationBuilder.DropColumn(
                name: "is_default",
                table: "jira_mappings");

            migrationBuilder.DropColumn(
                name: "sync_assignees",
                table: "jira_connections");

            migrationBuilder.AddPrimaryKey(
                name: "PK_jira_mappings",
                table: "jira_mappings",
                columns: new[] { "connection_id", "kind", "kanbada_value" });
        }
    }
}
