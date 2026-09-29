# Architecture and separation of concerns

## Runtime

The browser renders React components and calls relative `/api` URLs. During development Vite proxies those requests to ASP.NET Core on port 5180. PostgreSQL runs on port 55432. In production, a reverse proxy must serve the compiled portal and route `/api/*` to the API on the same public origin.

The API authenticates requests with protected cookies and checks workspace membership against PostgreSQL. UI visibility is not authorization. Files and opaque sharing tokens have separate database tables and permission checks.

## Frontend boundaries

- `portal/src/main.tsx`: mounts providers and global styles. It contains no workspace behavior.
- `portal/src/app/`: application shell, navigation, modal composition, and workspace orchestration.
- `portal/src/app/hooks/`: workspace loading/direct-link lifecycle and save coordination.
- `portal/src/features/`: cards, boards, projects, dashboards, help, workspaces, workflow, profile, notifications, and authentication views.
- `portal/src/domain/`: TypeScript models and card/project rules, independent of React, HTTP, and browser storage.
- `portal/src/infrastructure/`: HTTP, repository selection, sessions, attachment/sharing adapters, and account storage.
- `portal/src/infrastructure/local/`: browser-only workspace persistence and compatibility fixtures.
- `portal/src/shared/`: language and theme providers.
- `portal/src/styles/`: source CSS, explicit dark overrides, and generated dark CSS.

Views receive state and typed callbacks; the workspace shell coordinates changes affecting several features. Domain models do not import infrastructure. HTTP handling belongs to `apiClient`, and persistence belongs to the repository. Saves return canonical state rather than mutating React state objects. The `data.ts` barrel remains for compatibility with existing local browser checks; new source imports the owning module directly.

## Backend boundaries

The API is a deployable assembly with feature folders. The separate
`worker/Kanbada.Worker.csproj` process references it to share persistence, export, Jira and GitHub
synchronization logic without running HTTP endpoints. The public namespace remains `Kanbada.Api`.

- `Program.cs`: configuration entry point, middleware order, route composition, startup migration.
- `Configuration/`: dependency injection and validated options.
- `Documentation/`: Scalar integration, generated OpenAPI metadata, and fictional example payloads.
- `Cards/`: typed card-creation command, filtered cursor reads and SQL aggregate queries.
- `Security/` and `Errors/`: request-origin enforcement and centralized Problem Details.
- `Authentication/`: provider endpoints, password/session service, cookie validation.
- `Workspaces/`: workspace endpoints, transaction store, input validation, transport mapping, history generation, and EF metrics queries.
- `Persistence/`: EF Core context, explicit relational entities/configuration, and EF migrations.
- `Contracts/`: request records, initial workspace factory, JSON contract accessors.
- `Files/`, `Sharing/`, `Invitations/`: feature-specific transport and permission handling.
- `Health/`: PostgreSQL readiness check.
- `Exports/`: private durable export jobs, paged history, streaming downloads,
  snapshot-consistent XLSX generation and aggregate-based server PDF rendering.
- `Jira/`: owner-authorized project settings, token protection, cron scheduling,
  Jira REST adapters and per-item synchronization. The worker polls PostgreSQL
  schedules and claims connections using session advisory locks.
- `tests/`: API integration tests with isolated database schemas.

Endpoint modules translate HTTP into feature operations. `WorkspaceStore` owns the atomic workspace transaction. Validators and history generation do not know about HTTP or database connections. `WorkspaceMapper` translates the transport contract into tracked entities; `KanbadaDbContext` owns persistence configuration. `WorkspaceResolver` resolves the personal-workspace alias. `WorkspaceMetrics` executes database-side aggregates.

File, invitation, and sharing modules use EF queries and permission checks. If these workflows grow or gain another transport, extract services/repositories alongside the feature rather than adding unrelated logic to `Program.cs` or `WorkspaceStore`.

## Save flow

1. A view edits a draft copied from the current workspace.
2. The shell invokes the save hook, which prevents simultaneous local writes and adds any notification.
3. The remote repository compares the draft with the exact loaded version, omits server-controlled history/identity fields, and sends metadata changes plus ID-keyed card deltas using `/changes` and `If-Match`. Unloaded cards are never interpreted as deletions.
4. The API opens a transaction, checks membership and the EF concurrency token, validates references and protected resources, regenerates history, reconciles membership and file references, then increments the version in one transaction.
5. The returned canonical delta updates a cloned frontend snapshot, including server-generated history and version. Failure leaves the existing state and draft available; a stale save is rejected instead of merged blindly.

## Deliberate tradeoffs

Workspace business data is normalized into related PostgreSQL tables managed by EF Core. Portal bootstrap and refresh load metadata only. Cards and private notifications use version-bound keyset pagination; board columns load near their scroll boundary, while lists, calendar months and dashboard drilldowns offer load-more controls. SQL aggregates keep counts accurate without loading all cards. Direct links, sharing and attachment uploads do not load the entire card collection.

Ordinary card saves load and reconcile only the affected cards and associations, preserving their stored positions. Structural changes (project deletion, definition/member changes) still use the full server-side transaction to reconcile references and enforce existing policies; high-fanout structural writes remain more expensive than browsing or single-card edits. Metadata definitions/projects/members/activity remain eagerly loaded. Compatibility snapshot GET/PATCH/PUT remain available; the old synchronous workspace-export route returns 410 and points clients to the export queue.

The portal polls for changes every 15 seconds while no card or modal is being edited; it does not use WebSockets. Theme, language, sidebar collapse, and filter preferences belong to the browser. PostgreSQL stores business data and access control. The Exports area polls active jobs every three seconds and offers direct attachment links.

Export requests only create PostgreSQL jobs. An independent hosted service in the
worker processes them separately from Jira, using per-job advisory locks to
prevent duplicate execution across replicas. Interrupted jobs restart after the
connection releases its lock; three interrupted attempts produce an explicit failure.
Generation holds a repeatable-read snapshot starting at processing time.
XLSX streams 100-card pages into private temporary worksheet files and clears EF
tracking between pages; workspace notifications stream separately. Open XML SDK
builds the small workbook/style metadata; a create-mode ZIP streams the completed
worksheet files without buffering whole entries in read/write package mode.
Sheets split at Excel's row limit; long cells continue in an auxiliary sheet.
PDFsharp renders the
dashboard from metadata and complete SQL aggregates, not all card objects.
Progress updates use another connection so they remain visible during generation.
Completed files are stored in PostgreSQL chunks of at most 1 MiB, with completion
published only after all chunks are stored and membership is rechecked.
No shared API/worker filesystem is required. Temporary files are disposed after use.
Downloads stream under a repeatable-read transaction so concurrent cleanup cannot
truncate them. File bytes expire after seven days; small job history records remain.
Browser-only demo mode explicitly retains local exports because it has no server
workspace or worker. High-fanout structural commands remain a separate scaling concern.

## Reference guidance

The backend uses ASP.NET Core dependency injection, async I/O, pooled connections, centralized exception handling, cookie authentication, validated options, health checks, and OpenAPI. See [Microsoft's ASP.NET Core best practices](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices?view=aspnetcore-10.0). The frontend follows immutable state and named component/hook boundaries described in [React's rules](https://react.dev/reference/rules).

These are engineering practices, not a claim of compliance with every Microsoft standard or a substitute for a production security/accessibility review.
