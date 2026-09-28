using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueWorkspaceLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE workspaces, labels, card_labels IN SHARE ROW EXCLUSIVE MODE;
                CREATE TEMP TABLE label_merges ON COMMIT DROP AS
                SELECT workspace_id, id, survivor
                FROM (
                    SELECT workspace_id, id,
                        first_value(id) OVER (
                            PARTITION BY workspace_id, lower(btrim(name))
                            ORDER BY (id LIKE 'jira-%'), position, id
                        ) AS survivor
                    FROM labels
                ) ranked
                WHERE id <> survivor;

                INSERT INTO card_labels (workspace_id, card_id, label_id, position)
                SELECT cl.workspace_id, cl.card_id, m.survivor, min(cl.position)
                FROM card_labels cl
                JOIN label_merges m ON m.workspace_id = cl.workspace_id AND m.id = cl.label_id
                GROUP BY cl.workspace_id, cl.card_id, m.survivor
                ON CONFLICT (workspace_id, card_id, label_id) DO NOTHING;

                DELETE FROM card_labels cl USING label_merges m
                WHERE cl.workspace_id = m.workspace_id AND cl.label_id = m.id;
                DELETE FROM labels l USING label_merges m
                WHERE l.workspace_id = m.workspace_id AND l.id = m.id;
                UPDATE workspaces SET version = version + 1, updated_at = now()
                WHERE id IN (SELECT workspace_id FROM label_merges);
                """);

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "labels",
                type: "text",
                nullable: true,
                computedColumnSql: "lower(btrim(name))",
                stored: true);

            // Defer uniqueness until commit so valid multi-label renames can swap names.
            migrationBuilder.Sql("""
                ALTER TABLE labels ADD CONSTRAINT labels_workspace_normalized_name
                UNIQUE (workspace_id, normalized_name) DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE labels DROP CONSTRAINT labels_workspace_normalized_name;");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "labels");
        }
    }
}
