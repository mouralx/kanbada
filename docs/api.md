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

All feature routes begin with `/api`. Requests and responses use JSON except multipart file uploads, binary file downloads, and workspace JSON downloads. Authentication uses cookies; there is no bearer token API. All mutations require `X-Kanbada-Request: 1`. A supplied Origin must match the portal origin or API origin. The application intentionally does not enable cross-origin credentialed CORS.

An authenticated caller without access normally receives 404 for workspace/file lookup to avoid disclosing inaccessible resources. Ownership violations return 403. Invalid domain input returns 400. A missing workspace version returns 428; stale saves return 409. Rate-limited authentication requests return 429. Errors are centralized Problem Details with a trace ID; unexpected exception details are logged server-side.

## Routes

Authentication:

- `GET /auth/session`: current user or null, configured provider flags, and `twoFactorSetupRequired` / `twoFactorVerificationRequired` flags for restricted sessions.
- `POST /auth/register`: `{ "email": "...", "password": "...", "name": "..." }`; creates a personal workspace and a restricted setup session. Returns `twoFactorSetupRequired: true`; workspace access returns 403 until authenticator enrollment is confirmed. New external-provider identities follow the same requirement.
- `POST /auth/login`: email/password and optional `code`; returns 204 with a session on success. If 2FA is enabled and code is omitted, returns 200 `{ "twoFactorRequired": true }` without a session. Resubmit the credentials with an authenticator or recovery code. Invalid/replayed codes return 400; account lockout returns 429.
- `GET /auth/two-factor`: authenticated status (`available`, `enabled`, `recoveryCodesRemaining`, `required`, `passwordRequired`).
- `POST /auth/two-factor/setup`: authenticated `{ "password": "..." }`; returns a private `secret` and `uri` for enrollment, valid for ten minutes.
- `POST /auth/two-factor/confirm`: authenticated `{ "password": "...", "code": "..." }`; enables protection and returns `{ "codes": [...] }` once.
- `POST /auth/two-factor/recovery-codes`: same authenticated payload; replaces all recovery codes and returns the new set once.
- `POST /auth/two-factor/verify`: authenticated `{ "password": "", "code": "..." }`; upgrades the current restricted provider session after authenticator/recovery-code verification (204).
- `POST /auth/two-factor/disable`: same authenticated payload; disables protection for legacy optional accounts (204); mandatory accounts receive 403. Confirm, regenerate, and disable revoke other sessions.
- `POST /auth/logout`: revokes the session and clears the cookie.
- `GET /auth/{provider}/start?returnUrl=/...`: starts Google/Microsoft sign-in.
- `GET /auth/complete`: application callback after external middleware authentication.

Workspace reads and writes:

- `GET /workspaces`: workspaces available to the current user.
- `POST /workspaces`: `{ "name": "Workspace name" }`; returns its initial state.
- `GET /workspaces/{id}`: complete workspace snapshot, `version`, and ETag.
- `PATCH /workspaces/{id}`: only changed fields/array entries plus `If-Match`; returns canonical changes and the new version.
- `PUT /workspaces/{id}`: compatibility full-snapshot replacement; the portal does not use it.
- `DELETE /workspaces/{id}`: owner only; personal workspaces cannot be deleted.
- `GET /workspaces/{id}/export`: workspace JSON download.
- `GET /workspaces/{id}/{collection}`: projects, tasks, members, statuses, buckets, labels, swimlanes, notifications, or activity.
- `POST /workspaces/{id}/cards`: create a card from a typed command; only title is required.
- `GET /workspaces/{id}/cards/{cardId}`: one card, with case-insensitive ID lookup.
- `GET /workspaces/{id}/metrics?project=...&bucket=...&swimlane=...`: active total, completed, open, overdue, highPriority, and unassigned counts. Project is an ID; bucket and swimlane are stored names.

Projects, cards, comments, checklists, assignees, notifications, profile/workspace images, and workflow definitions are updated through compact versioned workspace PATCH operations. Related changes are grouped atomically, including a new card and its activity/notification. Standalone API clients can also use the dedicated card-creation command above. Labels/buckets/statuses are workspace-scoped; swimlanes reference a project.

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

The flexible workspace JSON schema is partly represented as an open object in generated OpenAPI. Consult `portal/src/domain/models.ts`, `api/Contracts/Models.cs`, and `WorkspaceValidator` for its fields and rules. The integration suite exercises actual snapshots. There is no paging, event subscription, public anonymous card endpoint, password reset endpoint, or email delivery service.

## Compact portal saves and conditional refreshes

The portal uses `PATCH /api/workspaces/{id}` with `X-Kanbada-Request: 1` and the loaded version in `If-Match`. It sends only changed fields or inserted/removed array entries, keeping related edits atomic:

```json
{"changes":[{"op":"replace","path":"/tasks/0/title","value":"Updated title"}]}
```

The supported operations are `add`, `remove`, and `replace`, with JSON Pointer paths (`~0` for `~`, `~1` for `/`). Array positions refer to the version identified by `If-Match`; operations are applied in order. There is no `move`, `copy`, `test`, script execution, or whole-document replacement. A request is limited to 10000 operations. Existing authorization, protected-resource validation, reference validation, and history generation apply. Invalid changes roll back; missing/stale versions return 428/409.

The response is `{ "version": 2, "changes": [...] }`, relative to the original loaded state. It includes changed fields and generated history, not unrelated cards, old history, or images. Apply it in order to a clone of that original state. Scalar documents this endpoint and has an executable example. The portal no longer calls the full-state PUT endpoint.

Workspace GET supports `If-None-Match: "2"`: after authorization, a matching version returns 304 and no body. The portal caches snapshots separately for each account/workspace and uses that cached snapshot only on an authenticated 304. HTTP caching is disabled with `private, no-store`; the application manages its explicit memory cache. A changed version still returns the full workspace, and initial loading remains a full snapshot.
