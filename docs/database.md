# PostgreSQL and Entity Framework Core

## Persistence boundaries

Kanbada uses EF Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL`. `KanbadaDbContext` is scoped to a request; the underlying `NpgsqlDataSource` pools connections. Explicit entity classes live in `api/Persistence/Entities/`, with keys, relationships, indexes, and constraints configured in the context. Runtime reads and writes use LINQ, tracked entities, `SaveChangesAsync`, and EF bulk deletion. SQL is confined to the one-time legacy import and database integration fixtures.

There are no JSON/JSONB business-data columns. JSON remains the HTTP/export format. `WorkspaceMapper` assembles that contract from relational records and synchronizes incoming collections by primary key. Existing rows retain their keys; EF only updates changed scalar values. The frontend sends compact field/array changes to PATCH `/api/workspaces/{id}`. Property names, card URLs, and the `If-Match` concurrency header remain compatible. The full PUT route remains available for compatibility and controlled imports.

## Tables and relationships

- `recovery_codes`: user-bound hashes of single-use authenticator recovery codes. Authenticator secrets and pending setup are encrypted in `users`; expiry, replay prevention, and failure-lockout state are stored alongside them. The per-account `two_factor_required` flag was removed by `EnforceTwoFactorForAllAccounts`; enrollment is mandatory for every user. `sessions.two_factor_verified` tracks assurance per session, and unverified/unenrolled sessions are restricted by middleware. Second-factor changes and login session creation serialize on the user row within an EF transaction.
- `users`, `identities`, `sessions`: accounts, provider subjects, and revocable session hashes. Email alone never links external identities. Password-account emails are unique and normalized to lowercase.
- `workspaces`: owner, personal flag, name, icon, banner, banner position, version, update timestamp. A partial unique index allows one personal workspace per owner.
- `members`: workspace/email key, registered user ID or pending invitation token, name, initials, color, photo, display order. This is authoritative for authorization.
- `projects`: workspace-scoped ID, name, color, description, archive flag, protected-project marker, display order.
- `statuses`, `buckets`, `labels`, `swimlanes`: separate workflow-definition tables. Swimlanes belong to a project.
- `cards`: workspace-scoped uppercase ID, project/status/bucket/swimlane foreign keys, title, description, priority, nullable PostgreSQL `date`, cover, display order.
- `card_assignees`, `card_labels`: ordered many-to-many relationships to members and labels.
- `card_comments`, `checklist_items`: ordered comments and checklist text/completion records.
- `card_history`, `history_changes`: ordered server-controlled audit entries and their individual change descriptions. Actor names are historical snapshots.
- `files`: file metadata and `bytea` content; `card_attachments` links cards to files. Board reads project metadata without loading binary content.
- `shares`: opaque token, card, creator, access mode, expiry, creation timestamp. One current link per card; deleting the card cascades to its link.
- `notifications`, `workspace_activity`: ordered notification and activity rows. Notification `at` retains the portal's display text; audit timestamps use UTC `timestamptz`.
- `__EFMigrationsHistory`: EF migration IDs and product versions.

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

The old `001_initial.sql` is retained only as migration input. `002_import_relational.sql` is a one-time backfill, not runtime document persistence. Do not edit applied migration files to introduce a new schema change.

Before upgrading an existing installation:

1. Stop the old API so it cannot write the old schema during conversion.
2. Take a PostgreSQL backup and verify restoration into a separate database.
3. Run `dotnet ef database update --project api` with a migration role.
4. Start the updated API and verify login, boards, card history, files, and shared links.

Automatic schema application is enabled by default only in Development. Production expects migrations to be applied before startup and refuses to start with pending migrations. `Database__ApplyMigrationsOnStartup=true` explicitly enables startup migration where appropriate. A normal production runtime role need not have schema-changing permissions.

For schema changes, edit entities/configuration, run `dotnet ef migrations add DescriptiveName --project api --output-dir Persistence/Migrations`, review the generated migration, and test both new and populated schemas. Deployment pipelines can generate a reviewed script with `dotnet ef migrations script --idempotent --project api --output migration.sql` or an EF migration bundle. The relational conversion intentionally has no automatic downgrade: reverting requires restoring the verified backup, with reconciliation of any later edits.

## Media and retention

Files retain the 25 MiB upload limit. Resized profile/workspace images are data-URL strings in dedicated text columns. Large media collections may warrant object storage; authorization must remain enforced on downloads.

The Compose volume is `kanbada-postgres-data`; stopping the container preserves it. Expired sessions are cleaned up on session issuance, share expiry is checked on reads, and abandoned uploads can be deleted by the uploader while unreferenced. There is no scheduled orphan-file retention job.

See Microsoft's guidance on [EF optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) and [applying migrations in production](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

New accounts set `users.PhotoRequired`; `users.Photo` stores the selected avatar data URI. Existing accounts retain optional photos. Registration avatar saves synchronize membership photos and increment affected workspace versions. New workspaces and accepted invitations inherit the account photo.
