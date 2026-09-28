# PostgreSQL and Entity Framework Core

## Persistence boundaries

Kanbada uses EF Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL`. `KanbadaDbContext` is scoped to a request; the underlying `NpgsqlDataSource` pools connections. Explicit entity classes live in `api/Persistence/Entities/`, with keys, relationships, indexes, and constraints configured in the context. Runtime reads and writes use LINQ, tracked entities, `SaveChangesAsync`, and EF bulk deletion. Explicit SQL is used for migrations, PostgreSQL synchronization locks, and database integration fixtures.

There are no JSON/JSONB business-data columns. JSON remains the HTTP/export format. `WorkspaceMapper` assembles that contract from relational records and synchronizes incoming collections by primary key. Existing rows retain their keys; EF only updates changed scalar values. The frontend sends compact field/array changes to PATCH `/api/workspaces/{id}`. Property names, card URLs, and the `If-Match` concurrency header remain compatible. The full PUT route remains available for compatibility and controlled imports.

## Tables and relationships

- `platform_branding`: singleton instance-wide name, validated main and optional
  collapsed-sidebar raster logos, visible-name preference, color
  palette, light/dark panel/text/border overrides, sidebar colors, font family,
  text scale, corner rounding, default theme and optimistic concurrency version.
  `ExpandedPlatformTheme` preserves existing values and adds automatic (null)
  optional colors with default typography (100%) and rounding (8). Platform admin
  account IDs are resolved from server configuration, not exposed in this table.
  `SeedDefaultPlatformBranding` inserts the built-in Kanbada defaults when the
  singleton row is absent and preserves any branding already saved.

- `recovery_codes`: user-bound hashes of single-use authenticator recovery codes. Authenticator secrets and pending setup are encrypted in `users`; expiry, replay prevention, and failure-lockout state are stored alongside them. The per-account `two_factor_required` flag was removed by `EnforceTwoFactorForAllAccounts`; enrollment is mandatory for every user. `sessions.two_factor_verified` tracks assurance per session, and unverified/unenrolled sessions are restricted by middleware. Second-factor changes and login session creation serialize on the user row within an EF transaction.
- `users`, `identities`, `sessions`: accounts, provider subjects, and revocable session hashes. Email alone never links external identities. Password-account emails are unique and normalized to lowercase.
- `workspaces`: owner, personal flag, name, icon, banner, banner position, version, update timestamp. A partial unique index allows one personal workspace per owner.
- `members`: workspace/email key, registered user ID or pending invitation token, name, initials, color, photo, display order. This is authoritative for authorization.
- `projects`: workspace-scoped ID, name, color, description, archive flag, protected-project marker, display order.
- `statuses`, `buckets`, `labels`, `swimlanes`: separate workflow-definition tables. Swimlanes belong to a project.
- `labels.normalized_name`: stored generated `lower(btrim(name))` value. The
  `labels_workspace_normalized_name` unique constraint prevents case/space variants
  within a workspace. It is deferred until transaction commit to allow valid name
  swaps in one save; the EF model represents its backing unique index.
- `cards`: workspace-scoped uppercase ID, project/status/bucket/swimlane foreign keys, title, description, priority, nullable PostgreSQL `date`, cover, display order.
- `card_assignees`, `card_labels`: ordered many-to-many relationships to members and labels.
- `card_comments`, `checklist_items`: ordered comments and checklist text/completion records.
- `card_history`, `history_changes`: ordered server-controlled audit entries and their individual change descriptions. Actor names are historical snapshots.
- `files`: file metadata and `bytea` content; `card_attachments` links cards to files. Board reads project metadata without loading binary content.
- `shares`: opaque token, card, creator, access mode, expiry, creation timestamp. One current link per card; deleting the card cascades to its link.
- `notifications`, `workspace_activity`: ordered notification and activity rows. Notification `at` retains the portal's display text; audit timestamps use UTC `timestamptz`.
- Assignment notifications use a non-null `recipient_id` and `card_id`, with the
  card title in `message` and an ISO UTC timestamp in `at`. Recipient filtering is
  server-side; writes preserve notifications belonging to other members.
  Existing shared notifications retain null recipient/card columns.
- `__EFMigrationsHistory`: EF migration IDs and product versions.
- `jira_connections`: one connection per workspace/project, encrypted token,
  edition, JQL, direction, cron/timezone and run status.
- `jira_mappings`: status/priority/assignee mappings keyed by connection, kind and
  Jira identity. Multiple Jira statuses can share a Kanbada status; `is_default`
  selects its outbound target. Assignee targets are registered workspace user
  UUIDs, resolved to current membership on each inbound sync.
- `jira_approved_hosts`: platform-administrator-approved HTTPS authorities. API and
  worker validate against current database approvals on every outbound request;
  no service restart is needed to approve or revoke a host.
- `jira_links`: stable card/issue associations, original creation system,
  last successful hashes and interrupted-creation state. Card IDs deliberately
  have no cascading card foreign key: links survive card deletion as tombstones,
  preventing accidental recreation. Project deletion cascades connector data,
  never remote Jira issues.

Composite foreign keys keep card relationships inside their workspace. A card's swimlane must also belong to its project. Database checks enforce uppercase card IDs, allowed priorities, normalized emails, and share access modes. Definition and member references must be reassigned/removed before those rows can be deleted. Deleting an authorized non-personal workspace removes its dependent data.

The transport contract references statuses, buckets, labels, swimlanes, and assignees by display name. Persistence resolves those to definition IDs or member email keys. Renames still update affected names in the incoming snapshot to keep that contract compatible. Project references already use IDs. Empty due-date strings map to SQL `NULL`.

`my-activities` remains the protected project ID in every workspace. `studio` is the owner's personal-workspace alias; the database uses a UUID. Protection of those resources belongs to `WorkspaceValidator` and `WorkspaceStore`.

## Transactions and concurrency

Workspace reads use a short repeatable-read transaction across the collection queries. Saves authorize and validate the incoming snapshot, regenerate history, synchronize tracked rows, remove unused files, and increment the workspace version in a transaction. `Version` is an EF concurrency token. A stale version, concurrent serialization conflict, or deadlock returns HTTP 409 without partial changes. Invite acceptance also increments the workspace version.

The portal sends only changes and reconstructs canonical state from the response delta. Initial loads and changed background refreshes still return a workspace snapshot; unchanged refreshes return body-free 304 responses. The server currently assembles workspace state internally to validate cross-record operations, and has no server-side paging or per-card versions. Server-side reads and change detection therefore grow with workspace size, although unchanged rows are not rewritten. Metrics use database-side EF aggregates with project, bucket, and swimlane filters.

## Migrations and existing data

The repository pins `dotnet-ef` in `dotnet-tools.json`. From the repository root:

```sh
dotnet tool restore
dotnet ef migrations list --project api
dotnet ef database update --project api
dotnet ef migrations has-pending-model-changes --project api
```

The design-time factory reads `api/appsettings.Local.json` and environment variables without starting the API. `ConnectionStrings__Postgres` takes precedence. Never put credentials in a committed command or script.

`RelationalStorage` adopts the original v1 schema or establishes it on an empty database, temporarily renames its tables, creates EF-managed tables, and imports every supported collection. Existing UUIDs, card/definition IDs, versions, account hashes, identities, sessions, invitations, file bytes, and share tokens are retained. It verifies legacy membership and optional card references, then removes the legacy tables and `schema_versions`. The conversion runs inside one migration transaction: inconsistent relationships fail and roll back instead of being silently dropped. `RelationalIntegrity` adds further database constraints.

`UniqueWorkspaceLabels` merges existing case/space variants in one transaction
before adding uniqueness. It prefers a non-Jira-generated ID, then the earliest
position and ID, keeping that definition's spelling, color and completion flag.
Card associations are reassigned before redundant definitions are removed;
overlapping associations become one membership. History text is unchanged.
Only affected workspace versions and update timestamps advance, preventing stale
clients from overwriting the repaired state. The migration takes write-blocking
locks, so stop both API writers and Jira workers for a controlled production upgrade.
Downgrading removes the constraint and generated column; it does not recreate
merged duplicates. Recovering the exact pre-merge state requires a backup.

`JiraFlexibleMappings` changes the mapping primary key to the Jira identity,
marks existing mappings as defaults, and adds `jira_connections.sync_assignees`
with a default of false. Existing directions and mapping values are preserved.
Downgrading refuses to discard assignee or many-to-one mappings: remove assignee
rows and reduce status groups to one Jira value first, or restore a backup.

`AssignmentNotifications` adds nullable recipient/card columns, a cascading
recipient-user foreign key, and a constraint requiring both fields together.
Assignment events are generated from newly added card-member associations in the
same workspace transaction, both for API writes and Jira imports. The migration
does not notify historical assignments. Downgrade is blocked while private
notifications exist, preventing their accidental conversion into shared notices.

The old `001_initial.sql` is retained only as migration input. `002_import_relational.sql` is a one-time backfill, not runtime document persistence. Do not edit applied migration files to introduce a new schema change.

Before upgrading an existing installation:

1. Stop the old API and Jira worker so they cannot write during conversion.
2. Take a PostgreSQL backup and verify restoration into a separate database.
3. Run `dotnet ef database update --project api` with a migration role.
4. Start the updated API and worker and verify login, boards, card history, files, and shared links.

Automatic schema application is enabled by default only in Development. Production expects migrations to be applied before startup and refuses to start with pending migrations. `Database__ApplyMigrationsOnStartup=true` explicitly enables startup migration where appropriate. A normal production runtime role need not have schema-changing permissions.

For schema changes, edit entities/configuration, run `dotnet ef migrations add DescriptiveName --project api --output-dir Persistence/Migrations`, review the generated migration, and test both new and populated schemas. Deployment pipelines can generate a reviewed script with `dotnet ef migrations script --idempotent --project api --output migration.sql` or an EF migration bundle. The relational conversion intentionally has no automatic downgrade: reverting requires restoring the verified backup, with reconciliation of any later edits.

## Media and retention

Files retain the 25 MiB upload limit. Resized profile/workspace images are data-URL strings in dedicated text columns. Large media collections may warrant object storage; authorization must remain enforced on downloads.

The Compose volume is `kanbada-postgres-data`; stopping the container preserves it. Expired sessions are cleaned up on session issuance, share expiry is checked on reads, and abandoned uploads can be deleted by the uploader while unreferenced. There is no scheduled orphan-file retention job.

See Microsoft's guidance on [EF optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) and [applying migrations in production](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

New accounts set `users.PhotoRequired`; `users.Photo` stores the selected avatar data URI. Existing accounts retain optional photos. Registration avatar saves synchronize membership photos and increment affected workspace versions. New workspaces and accepted invitations inherit the account photo.
