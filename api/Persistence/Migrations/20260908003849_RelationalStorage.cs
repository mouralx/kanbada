using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanbada.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelationalStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Establish the old schema for a fresh database, or adopt the existing v1 schema.
            migrationBuilder.Sql(ReadSql("001_initial.sql"));
            migrationBuilder.RenameTable(name: "users", newName: "legacy_users");
            migrationBuilder.RenameTable(name: "identities", newName: "legacy_identities");
            migrationBuilder.RenameTable(name: "sessions", newName: "legacy_sessions");
            migrationBuilder.RenameTable(name: "workspaces", newName: "legacy_workspaces");
            migrationBuilder.RenameTable(name: "members", newName: "legacy_members");
            migrationBuilder.RenameTable(name: "files", newName: "legacy_files");
            migrationBuilder.RenameTable(name: "shares", newName: "legacy_shares");
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identities",
                columns: table => new
                {
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identities", x => new { x.provider, x.subject });
                    table.ForeignKey(
                        name: "FK_identities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personal = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: true),
                    banner = table.Column<string>(type: "text", nullable: true),
                    banner_position = table.Column<double>(type: "double precision", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspaces_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "buckets",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buckets", x => new { x.workspace_id, x.id });
                    table.ForeignKey(
                        name: "FK_buckets_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.id);
                    table.UniqueConstraint("AK_files_workspace_id_id", x => new { x.workspace_id, x.id });
                    table.ForeignKey(
                        name: "FK_files_users_uploader_id",
                        column: x => x.uploader_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_files_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "labels",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labels", x => new { x.workspace_id, x.id });
                    table.ForeignKey(
                        name: "FK_labels_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "members",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invite_token = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    initials = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    photo = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_members", x => new { x.workspace_id, x.email });
                    table.ForeignKey(
                        name: "FK_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_members_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => new { x.workspace_id, x.id });
                    table.ForeignKey(
                        name: "FK_notifications_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    system = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => new { x.workspace_id, x.id });
                    table.ForeignKey(
                        name: "FK_projects_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "statuses",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statuses", x => new { x.workspace_id, x.id });
                    table.ForeignKey(
                        name: "FK_statuses_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_activity",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_activity", x => new { x.workspace_id, x.position });
                    table.ForeignKey(
                        name: "FK_workspace_activity_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "swimlanes",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_swimlanes", x => new { x.workspace_id, x.id });
                    table.ForeignKey(
                        name: "FK_swimlanes_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_swimlanes_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cards",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    status_id = table.Column<string>(type: "text", nullable: false),
                    bucket_id = table.Column<string>(type: "text", nullable: true),
                    swimlane_id = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<string>(type: "text", nullable: false),
                    due = table.Column<DateOnly>(type: "date", nullable: true),
                    cover = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cards", x => new { x.workspace_id, x.id });
                    table.CheckConstraint("cards_uppercase_id", "id ~ '^KB-[A-Z0-9-]+$'");
                    table.ForeignKey(
                        name: "FK_cards_buckets_workspace_id_bucket_id",
                        columns: x => new { x.workspace_id, x.bucket_id },
                        principalTable: "buckets",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cards_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cards_statuses_workspace_id_status_id",
                        columns: x => new { x.workspace_id, x.status_id },
                        principalTable: "statuses",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cards_swimlanes_workspace_id_swimlane_id",
                        columns: x => new { x.workspace_id, x.swimlane_id },
                        principalTable: "swimlanes",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cards_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_assignees",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    member_email = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_assignees", x => new { x.workspace_id, x.card_id, x.member_email });
                    table.ForeignKey(
                        name: "FK_card_assignees_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_card_assignees_members_workspace_id_member_email",
                        columns: x => new { x.workspace_id, x.member_email },
                        principalTable: "members",
                        principalColumns: new[] { "workspace_id", "email" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_card_assignees_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_attachments",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_attachments", x => new { x.workspace_id, x.card_id, x.file_id });
                    table.ForeignKey(
                        name: "FK_card_attachments_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_card_attachments_files_workspace_id_file_id",
                        columns: x => new { x.workspace_id, x.file_id },
                        principalTable: "files",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_card_attachments_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_comments",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_comments", x => new { x.workspace_id, x.card_id, x.position });
                    table.ForeignKey(
                        name: "FK_card_comments_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_card_comments_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_history",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_history", x => new { x.workspace_id, x.card_id, x.id });
                    table.ForeignKey(
                        name: "FK_card_history_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_card_history_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_labels",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    label_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_labels", x => new { x.workspace_id, x.card_id, x.label_id });
                    table.ForeignKey(
                        name: "FK_card_labels_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_card_labels_labels_workspace_id_label_id",
                        columns: x => new { x.workspace_id, x.label_id },
                        principalTable: "labels",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_card_labels_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checklist_items",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    done = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checklist_items", x => new { x.workspace_id, x.card_id, x.position });
                    table.ForeignKey(
                        name: "FK_checklist_items_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_checklist_items_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shares",
                columns: table => new
                {
                    token = table.Column<string>(type: "text", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shares", x => x.token);
                    table.CheckConstraint("shares_access", "access IN ('signed-in','members')");
                    table.ForeignKey(
                        name: "FK_shares_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shares_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shares_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "history_changes",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "text", nullable: false),
                    history_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_history_changes", x => new { x.workspace_id, x.card_id, x.history_id, x.position });
                    table.ForeignKey(
                        name: "FK_history_changes_card_history_workspace_id_card_id_history_id",
                        columns: x => new { x.workspace_id, x.card_id, x.history_id },
                        principalTable: "card_history",
                        principalColumns: new[] { "workspace_id", "card_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_history_changes_cards_workspace_id_card_id",
                        columns: x => new { x.workspace_id, x.card_id },
                        principalTable: "cards",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_history_changes_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_assignees_workspace_id_member_email",
                table: "card_assignees",
                columns: new[] { "workspace_id", "member_email" });

            migrationBuilder.CreateIndex(
                name: "IX_card_attachments_workspace_id_file_id",
                table: "card_attachments",
                columns: new[] { "workspace_id", "file_id" });

            migrationBuilder.CreateIndex(
                name: "IX_card_labels_workspace_id_label_id",
                table: "card_labels",
                columns: new[] { "workspace_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_bucket_id",
                table: "cards",
                columns: new[] { "workspace_id", "bucket_id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_due",
                table: "cards",
                columns: new[] { "workspace_id", "due" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_project_id",
                table: "cards",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_status_id",
                table: "cards",
                columns: new[] { "workspace_id", "status_id" });

            migrationBuilder.CreateIndex(
                name: "IX_cards_workspace_id_swimlane_id",
                table: "cards",
                columns: new[] { "workspace_id", "swimlane_id" });

            migrationBuilder.CreateIndex(
                name: "IX_files_uploader_id",
                table: "files",
                column: "uploader_id");

            migrationBuilder.CreateIndex(
                name: "IX_identities_user_id",
                table: "identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_members_invite_token",
                table: "members",
                column: "invite_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_members_user_id",
                table: "members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_members_workspace_id_user_id",
                table: "members",
                columns: new[] { "workspace_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_user_id",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_shares_creator_id",
                table: "shares",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "IX_shares_workspace_id_card_id",
                table: "shares",
                columns: new[] { "workspace_id", "card_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_swimlanes_workspace_id_project_id",
                table: "swimlanes",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "password_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_owner_id",
                table: "workspaces",
                column: "owner_id",
                unique: true,
                filter: "personal");
            migrationBuilder.Sql(ReadSql("002_import_relational.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore the pre-migration backup to revert the relational conversion; automatic downgrade would lose subsequent edits.");
        }

        private static string ReadSql(string name)
        {
            using var stream = typeof(RelationalStorage).Assembly.GetManifestResourceStream("Kanbada.Api.Persistence.Migrations." + name)
                ?? throw new InvalidOperationException("Missing migration resource: " + name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
