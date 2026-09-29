# API and Scalar

## Interactive reference

Open `/api/scalar` through the portal at `http://localhost:4173/api/scalar` or directly at `http://127.0.0.1:5180/api/scalar`. Scalar uses the generated `/api/openapi.json`, groups operations by feature, serves its JavaScript locally, and defaults code samples to C# HttpClient. The optional Scalar agent and external default fonts are disabled.

When opening Scalar through the portal, an existing portal session is available to same-origin browser requests. Otherwise execute `/api/auth/login` in Scalar with your development account first. The session is an HttpOnly cookie; do not copy it into source code or localStorage. Mutations expose the required `X-Kanbada-Request` header with default value `1`. For workspace PATCH (or compatibility PUT), copy the current `version` from GET into `If-Match`.

Scalar and OpenAPI are currently mapped in every environment. Restrict their routes at the deployment boundary if your environment requires private developer documentation. See [Scalar's ASP.NET Core integration](https://scalar.com/products/api-references/integrations/aspnetcore/integration).

## Create your first card in Scalar

1. Open `/api/scalar` on the same origin where you signed in, or use **Authentication → Sign in with an email and password** first.
2. Open **Cards → Create a card** (`POST /api/workspaces/{id}/cards`).
3. Set `id` to `studio` and leave `X-Kanbada-Request` as `1`.
4. Select the **minimal** request example and replace its title:

```json
{ "title": "My first card" }
```

5. Send the request. A 201 response contains the generated uppercase card ID and history. `Location` points to the card's GET route; `ETag` contains the new workspace version.

Only `title` is required. Defaults are My activities, the workspace's first status, Medium priority, no due date, and empty assignment/label/checklist/comment/attachment collections. The **detailed** example shows those optional fields. Use existing project IDs, member names, and workflow names when overriding defaults. This creation command does not require `If-Match`; competing saves can still return 409 without overwriting changes.

Every operation includes a request URL example, parameter examples, documented success/error responses, and JSON or multipart examples when it takes a body. No-body operations explicitly say so. The collection endpoint includes response examples for every collection. Fictional IDs/tokens are not live resources: use values returned by your own API.

`api/Documentation/ApiOperationCatalog.cs` owns descriptions and example URLs, `ApiExamples.cs` owns fictional data, and `OperationDocumentation.cs` adds them to generated OpenAPI. A coverage test rejects undocumented operations or missing parameter/body/response examples.

## Transport conventions

All feature routes begin with `/api`. Requests and responses use JSON except multipart file uploads and binary downloads (including XLSX/PDF exports). Authentication uses cookies; there is no bearer token API. All mutations require `X-Kanbada-Request: 1`. A supplied Origin must match the portal origin or API origin. The application intentionally does not enable cross-origin credentialed CORS.

An authenticated caller without access normally receives 404 for workspace/file lookup to avoid disclosing inaccessible resources. Ownership violations return 403. Invalid domain input returns 400. A missing workspace version returns 428; stale saves return 409. Rate-limited authentication requests return 429. Errors are centralized Problem Details with a trace ID; unexpected exception details are logged server-side.

## Routes

Authentication:

- `GET /auth/session`: current user or null, configured provider flags, and `twoFactorSetupRequired` / `twoFactorVerificationRequired` flags for restricted sessions.
- `POST /auth/register`: `{ "email": "...", "password": "...", "name": "..." }`; creates a personal workspace and a restricted setup session. Returns `avatarRequired: true` and `twoFactorSetupRequired: true`; workspace access returns 403 until a photo is saved and authenticator enrollment is confirmed. New external-provider identities follow the same requirement.
- `POST /auth/login`: email/password plus `code` for enrolled accounts; returns 204 with a verified session on success. Unenrolled accounts receive only a restricted setup session. If an enrolled account omits code, returns 200 `{ "twoFactorRequired": true }` without a session. Resubmit the credentials with an authenticator or recovery code. Invalid/replayed codes return 400; account lockout returns 429.
- `GET /auth/two-factor`: authenticated status (`available`, `enabled`, `recoveryCodesRemaining`, `required`, `passwordRequired`).
- `POST /auth/two-factor/setup`: authenticated `{ "password": "..." }`; returns a private `secret` and `uri` for enrollment, valid for ten minutes.
- `POST /auth/two-factor/confirm`: authenticated `{ "password": "...", "code": "..." }`; enables protection and returns `{ "codes": [...] }` once.
- `POST /auth/two-factor/recovery-codes`: same authenticated payload; replaces all recovery codes and returns the new set once.
- `POST /auth/two-factor/verify`: authenticated `{ "password": "", "code": "..." }`; upgrades the current restricted provider session after authenticator/recovery-code verification (204).
- `POST /auth/two-factor/disable`: always returns 403; 2FA is mandatory for all accounts.
- `POST /auth/change-password`: authenticated `{ "password": "...", "newPassword": "...", "code": "..." }`; requires the current password and an unused authenticator or recovery code. New passwords must be 12–200 characters. Returns 204 and revokes other sessions. External accounts must change passwords with their provider.
- `POST /auth/logout`: revokes the session and clears the cookie.
- `GET /auth/{provider}/start?returnUrl=/...`: starts Google/Microsoft sign-in.
- `GET /auth/complete`: application callback after external middleware authentication.

Workspace reads and writes:

- `GET /workspaces`: workspaces available to the current user.
- `POST /workspaces`: `{ "name": "Workspace name" }`; returns its initial state.
- `GET /workspaces/{id}?metadataOnly=true`: portal metadata, `version`, ETag and notification count, without cards or private notification payloads. Omit the flag for the compatibility full snapshot.
- `GET /workspaces/{id}/cards`: filtered cursor page (`items`, `total`, `nextCursor`, `version`); 40 cards by default, maximum 100.
- `GET /workspaces/{id}/card-summary`: complete filtered counts and grouped dashboard data, calculated in PostgreSQL.
- `PATCH /workspaces/{id}/changes`: metadata changes and ID-keyed card edits/deletions, safe for partially loaded workspaces; requires `If-Match`.
- `GET /workspaces/{id}/notification-feed`: 40 visible notifications per cursor page.
- `DELETE /workspaces/{id}/notification-feed?notificationId=...`: dismiss a visible notification, or omit `notificationId` to clear all visible notifications (including unloaded pages); requires `If-Match`.
- `PATCH /workspaces/{id}`: only changed fields/array entries plus `If-Match`; returns canonical changes and the new version.
- `PUT /workspaces/{id}`: compatibility full-snapshot replacement; the portal does not use it.
- `DELETE /workspaces/{id}`: owner only; personal workspaces cannot be deleted.
- `POST /workspaces/{id}/exports`: queue a private export (202).
- `GET /workspaces/{id}/exports`: your export history, 20 jobs per cursor page.
- `GET /workspaces/{id}/exports/{exportId}`: status, progress, errors and expiry.
- `GET /workspaces/{id}/exports/{exportId}/download`: stream a completed file.
- `GET /workspaces/{id}/export`: retired synchronous endpoint (410); use the export queue.
- `GET /workspaces/{id}/{collection}`: projects, tasks, members, statuses, buckets, labels, swimlanes, notifications, or activity.
- `POST /workspaces/{id}/cards`: create a card from a typed command; only title is required.
- `GET /workspaces/{id}/cards/{cardId}`: one card, with case-insensitive ID lookup.
- `GET /workspaces/{id}/metrics?project=...&bucket=...&swimlane=...`: active total, completed, open, overdue, highPriority, and unassigned counts. Project is an ID; bucket and swimlane are stored names.

Projects, cards, comments, checklists, assignees, notifications, profile/workspace images, and workflow definitions are updated through compact versioned workspace PATCH operations. Related changes are grouped atomically, including a new card and its activity/notification. Standalone API clients can also use the dedicated card-creation command above. Labels/buckets/statuses are workspace-scoped; swimlanes reference a project.

Cards expose a server-derived `readOnly` flag. Confirmed Jira-linked cards in
Jira-to-Kanbada mode reject user edits, moves and deletion with HTTP 403, including
changes to comments, checklists and attachments. PATCH cannot write `readOnly`;
PUT cannot bypass the policy by omitting it. Unlinked cards remain editable.

New assignments to registered members generate private assignment notifications
in the same transaction. Workspace/notification reads return only the caller's
assignment notifications alongside existing shared workspace notices. Assignment
notifications include `cardId`; their `message` contains the assigned card's title.
They can be dismissed through the normal notification-array changes, but cannot
be forged or edited. Clearing your notifications does not clear another member's
private notifications. Repeated saves/syncs with unchanged assignees do not notify
again; unassigning and later reassigning creates a new notification.

Files:

- `POST /workspaces/{id}/files`: multipart field `file`, maximum 25 MiB; returns attachment metadata.
- `GET /files/{id}`: authorized attachment download, forced as a file.
- `DELETE /files/{id}`: removes an unreferenced upload owned by the caller; idempotent 204.

Add returned metadata to a card and save the workspace. Removing the final reference from saved cards deletes the stored file in the workspace transaction. Cancelling a draft attempts cleanup of its unreferenced uploads.

Sharing:

- `GET /workspaces/{id}/cards/{cardId}/share`: current link or null.
- `POST /workspaces/{id}/cards/{cardId}/share`: `{ "access": "signed-in" | "members", "days": 0 | 7 | 30 }`; issues/replaces a link.
- `DELETE /shares/{token}`: revokes a workspace's link.
- `GET /shares/{token}`: authenticated read-only card data and display context.
- `GET /shares/{token}/files/{id}`: download only files referenced by that shared card.

Invitations:

- `GET /invitations/{token}`: invitation details for an authenticated user.
- `POST /invitations/{token}/accept`: verifies the invited email against the session and adds membership.

Operations:

- `GET /health`: basic database connectivity.
- `GET /health/ready`: standard ASP.NET Core PostgreSQL health check.
- `GET /openapi.json`: generated contract.
- `GET /scalar`: interactive reference.

## Workspace versioning example

Read `/api/workspaces/studio`. Keep that immutable base snapshot, send only changes with `If-Match: 4` if the GET returned version 4, and apply the canonical response delta to that base. Successful PATCH returns version 5. The request must not include old history or unrelated fields.

If another member saved version 5 first, your PATCH returns 409. Reload the workspace and reconcile the user's intended changes. Never automatically resubmit old indexed changes with a newer version: their targets may have changed.

## Current contract limits

The flexible workspace JSON schema is partly represented as an open object in generated OpenAPI. Consult `portal/src/domain/models.ts`, `api/Contracts/Models.cs`, and `WorkspaceValidator` for its fields and rules. The integration suite exercises actual snapshots. There is no event subscription, public anonymous card endpoint, password reset endpoint, or email delivery service.

## Compact portal saves and conditional refreshes

The portal uses `PATCH /api/workspaces/{id}/changes` with `X-Kanbada-Request: 1` and the loaded version in `If-Match`. Metadata retains JSON Pointer changes. Existing cards use ID-keyed field changes against a one-card array; new cards use `{ "id": "...", "value": { ... } }`, and deletions use `removed` IDs. Omitted cards are never deleted:

```json
{"changes":[],"upserts":[{"id":"KB-A1B2C3D4","changes":[{"op":"replace","path":"/tasks/0/title","value":"Updated title"}]}],"removed":[]}
```

The supported operations are `add`, `remove`, and `replace`, with JSON Pointer paths (`~0` for `~`, `~1` for `/`). Array positions refer to the version identified by `If-Match`; operations are applied in order. There is no `move`, `copy`, `test`, script execution, or whole-document replacement. A request is limited to 10000 operations. Existing authorization, protected-resource validation, reference validation, and history generation apply. Invalid changes roll back; missing/stale versions return 428/409.

The response is `{ "version": 2, "changes": [...], "cards": [{ "id": "...", "changes": [...] }], "removed": [...] }`. Apply metadata changes to the metadata snapshot and each card delta to its original one-card array (an empty array for a new card). Optional `retained` card IDs request canonical deltas for open/cached cards even when only metadata changed. It includes generated history, not unrelated cards, old history, or images. Structural edits reconcile references on cards not downloaded by the browser. The original indexed `PATCH /workspaces/{id}` and full-state PUT remain for compatibility; do not use them with partial snapshots.

Workspace GET supports `If-None-Match: "2"`: after authorization, a matching version returns 304 and no body. The portal caches metadata separately for each account/workspace and uses that cached snapshot only on an authenticated 304. HTTP caching is disabled with `private, no-store`; the application manages its explicit memory cache. Initial loading and changed refreshes request `metadataOnly=true`, never the full card collection. API clients must keep metadata/full-snapshot caches separate.

## Pagination and complete results

Card reads and summaries accept `project` (ID), `mine=true`, `active=true`, `search`,
`priority`, `person` (assignee name), `bucket` (name), `swimlane` (ID), `status`
(name), `completion` (`Open`, `Completed`, `Overdue`), `from`/`to` (inclusive due
dates), `today` (caller-local date) and `unassigned=true`. Omit a filter for all
values; an empty bucket/swimlane selects ungrouped cards. Filters are applied
before pagination, including searches for cards that have never been loaded.

Pass `nextCursor` as URL-encoded `after` with the same filters. Pages use indexed
`position, id` ordering, not increasingly expensive offsets. Totals refer to all
matches, not just the page. The cursor is scoped to workspace, caller, filters
and workspace version. Changed versions return 409 rather than silently skipping
or duplicating cards; restart the query. Invalid cursors/page sizes return 400.

The board fetches each visible column/swimlane independently and loads the next
40 cards when its bottom approaches the viewport. Lists, calendar months,
dashboard drilldowns and notifications expose explicit load-more controls.
Changing filters, view, workspace or version discards old pages and cancels
pending requests. Closing/reopening a card uses its independent detail read.

Dashboard totals/charts, sidebar counts, project progress and definition usage
come from database aggregates. Project XLSX, workspace XLSX and dashboard PDF
exports run asynchronously in the separate worker, never in the browser or an
HTTP request. Submit `{ "id": "<new UUID>", "kind": "project-xlsx",
"query": { "project": "my-activities" }, "locale": "en-US" }`; supported kinds
are `project-xlsx`, `workspace-xlsx` and `dashboard-pdf`. A repeated UUID with the
same parameters returns the existing job; conflicting reuse returns 409.
Project XLSX includes every card in that project, regardless of board filters.
Workspace XLSX includes all cards and only notifications visible to the requester.
Invitation tokens are deliberately excluded; these are spreadsheets, not restorable backups.
Workbooks contain Cards, Projects, Card labels, Assignees, Comments, Checklist,
History, History changes and Attachments sheets. Workspace exports also include
Workspace, Snapshot, Members, Statuses, Buckets, Labels, Swimlanes, Activity and
Notifications. Attachment bytes are not embedded. Sheet names and column keys
are stable English identifiers; user content remains unchanged.
Each sheet includes a header row and splits automatically at Excel's 1,048,576-row
limit. Text exceeding 32,767 UTF-16 units is preserved in ordered `Long text` rows,
identified by sheet/row/column/part; the original cell contains the first part.
Booleans and numbers have native cell types; user strings are never formulas.
Old JSON export kinds now return 400. Existing JSON files expire during the
`XlsxExportFormats` migration, and pending JSON jobs are requeued as XLSX.
Security recovery-code downloads remain TXT and are never added to export history.
Dashboard PDF uses the same `CardQuery` filters as its dashboard, including
the caller-local `today`, and complete SQL aggregates. Locales are `en-US` and `pt-PT`.

Jobs transition from `queued` to `running`, then `completed` or `failed`.
`processed`, `total`, `snapshotVersion`, `error` and timestamps describe progress.
The snapshot is taken when processing starts, not when the request is queued.
Concurrent edits do not mix versions or abort a consistent export. Only the
requester with current workspace membership can follow or download it.
Files expire **7 days after completion**; expiry is enforced even when the
worker is offline. Downloads return 409 before completion, 410 after expiry,
and 404 for inaccessible jobs. Download using an ordinary authenticated browser
attachment link, not `fetch` followed by a Blob. History/status are private/no-store.
An already-started download can finish while expired chunks are cleaned up.

Registration avatars: `GET /auth/avatar/gravatar` returns `{ "photo": "data:..." }` or `{ "photo": null }` for the signed-in account email. `POST /auth/avatar` accepts `{ "photo": "data:image/png;base64,..." }` or `{ "useGravatar": true }` and returns 204. Both require a session, including restricted registration sessions. Images must be PNG, JPEG, GIF or WebP, up to 2 MB. Gravatar is optional; selecting it stores a snapshot. Session responses include `avatarRequired`.
