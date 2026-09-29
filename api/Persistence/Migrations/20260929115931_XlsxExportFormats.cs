using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class XlsxExportFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM export_chunks
                WHERE export_id IN (SELECT id FROM exports WHERE kind IN ('project-json', 'workspace-json'));
                UPDATE exports SET
                    kind = replace(kind, '-json', '-xlsx'),
                    status = CASE WHEN status IN ('queued', 'running') THEN 'queued' ELSE 'expired' END,
                    bytes = 0, processed = 0, total = NULL, attempts = 0, snapshot_version = NULL,
                    started_at = NULL,
                    completed_at = CASE WHEN status IN ('queued', 'running') THEN NULL ELSE completed_at END,
                    expires_at = CASE WHEN status IN ('queued', 'running') THEN NULL ELSE now() END,
                    error = CASE WHEN status IN ('queued', 'running') THEN NULL
                        ELSE 'JSON exports were replaced by XLSX. Request a new export.' END
                WHERE kind IN ('project-json', 'workspace-json');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Generated files cannot be converted back; a downgrade requires new export requests.
        }
    }
}
