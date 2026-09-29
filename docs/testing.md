# Testing and contribution standards

## Required local checks

From the repository root:

```sh
npm --prefix portal ci
npm --prefix portal run check
npm --prefix portal run build
dotnet restore api/Kanbada.Api.csproj --locked-mode
dotnet format whitespace api/Kanbada.Api.csproj --no-restore --verify-no-changes
```

With PostgreSQL running:

```sh
dotnet test api/tests/Kanbada.Api.Tests/Kanbada.Api.Tests.csproj
```

With API, worker and portal running at the documented ports:

```sh
npm --prefix portal test
```

Install the browser once if Playwright requests it:

```sh
cd portal
npx playwright install chromium
```

The frontend check runs strict TypeScript, ESLint, and Prettier. ESLint enforces unused-variable checks and React hook ordering/dependencies. The root `.editorconfig` defines common text conventions; C# uses four spaces, frontend code uses two. Do not manually compress source files. NuGet and npm lockfiles are committed inputs for reproducible restore.

## CI

`.github/workflows/quality.yml` runs the same frontend checks, C# formatting, API integration tests, and API-backed browser check on pushes and pull requests. It uses a disposable PostgreSQL service. The workflow is supplied for GitHub-hosted repositories; local validation does not imply the remote workflow has already run.

## API coverage

The xUnit suite uses `WebApplicationFactory`, the real middleware pipeline, and PostgreSQL. Each fixture creates a random `test_<uuid>` schema and drops only that schema afterward. Set `ConnectionStrings__Postgres` or use the local setup file. The test database user needs permission to create schemas. Tests must never truncate a shared application's tables.

Coverage includes cookie login/logout and revocation, Problem Details, workspace invariants, malformed JSON field types, stale-write rejection, optional due dates and metrics, invitation acceptance/removal, self-profile updates, file authorization, sharing replacement/revocation, origin rejection, OpenAPI headers, dedicated card creation/defaults, example coverage for every operation, and Scalar's page/configuration.

External Google/Microsoft flows require registered applications and are not automated by the local suite. Review them against dedicated provider test registrations when changing authentication middleware.

Jira tests use a fake HTTP handler with the real database to exercise Cloud/Data
Center authentication and pagination, field conversion, one-way and bidirectional
conflicts, origin preservation, idempotency, interrupted creation, tombstones and
cron time zones/calendar schedules. No real Jira credentials are needed.
Label regression cases cover reusing an existing definition across case/space
variants, multiple variants on one issue, shared definitions across issues,
preserved spelling/color, and repeated synchronization without unnecessary writes.
Card-link tests cover workspace membership, missing cards, unlinked/pending issues,
disabled synchronization, Data Center context paths/ports, and Cloud browser URLs.
Flexible-mapping tests cover migration compatibility, many-to-one status persistence,
default validation, preserving already-matching Jira statuses, inbound-only
assignees, unmapped-user warnings, clearing unassigned issues, mapping fixes without
other field changes, and disabled/outbound-only assignment preservation. Fake Jira
asserts that issue creation/update bodies never contain an assignee.
Run just this feature with
`dotnet test api/tests/Kanbada.Api.Tests/Kanbada.Api.Tests.csproj --filter FullyQualifiedName~Jira`.

GitHub tests use a fake GraphQL HTTP handler with real PostgreSQL, never live
GitHub credentials or writes. They cover Projects v2 field/item/nested pagination,
all three content types, one-way/bidirectional updates, encrypted settings,
owner/member permissions, Enterprise approval/revocation, optional fields,
assignment notifications, read-only cards, tombstones, interrupted issue creation,
idempotent project enrollment, partial-write recovery, concurrent local edits,
worker locks, rate-limit reset times and pausing without network access.
Run them with
`dotnet test api/tests/Kanbada.Api.Tests --filter FullyQualifiedName~GitHub`.

## Browser coverage

`npm --prefix portal run test:github` covers GitHub field/default/assignee mapping
controls, saved-run queuing, pausing without remote access, explicit creation retry
confirmation, card links/read-only controls, and light/dark layouts down to 320 px.
GitHub endpoints are mocked in this browser script; it creates only a development
account and local card. Real endpoint persistence and recovery are covered by the
mock-HTTP/database tests above. Run it separately from other browser suites to
stay within authentication rate limits.

`npm --prefix portal run test:branding` exercises branding preview, logo upload,
logo-only centering and accessible names, distinct expanded/collapsed logos,
mobile logo selection and fallback when the collapsed logo is removed,
colors, SVG-to-PNG conversion (aspect ratio, transparency and script isolation),
panel/sidebar overrides, font and radius controls, desktop/mobile layout down to
320 pixels, draft reset, personal-theme precedence and branded
sign-in. It mocks platform-admin reads/writes so it never changes a shared
installation's branding. `BrandingTests` exercises the real API/database,
authorization (including enrollment sessions), validation, conditional reads,
concurrency, reset and existing-account administrator bootstrap in isolated schemas.

Core browser checks also cover sharing load errors and retry, first-time link
creation, reloading saved access/expiration, revocation and dropdown sizing.
The API sharing regression requires an explicit JSON `null` response when no
link exists, rather than an empty successful response.

`npm --prefix portal run test:pagination` uses 125 cards to verify empty-card
bootstrap, 40-card board batches, scroll-triggered downloads, remote search,
list paging, complete dashboard totals, queued XLSX/PDF exports, direct links to unloaded
cards, edits preserving other cards, and paged notification clearing. Run it
separately from the main browser suite to avoid the shared development server's
20-authentication-requests/minute limit (wait a minute between suites).

Export browser checks require the **worker** alongside the API and portal.
They follow jobs in Exports, download completed files, check history survives
reloads, and ensure API-mode exports neither fetch all card pages nor load the
browser PDF renderer. `ExportTests` uses small multi-page datasets to cover
private ownership, concurrent idempotent submissions, complete XLSX/chunks,
snapshot consistency during edits, bounded EF tracking, filtered Portuguese PDFs,
interrupted-job recovery, advisory locks, failure reporting, history pagination
and seven-day expiry/cleanup. XLSX files are validated with the Open XML validator;
small limits exercise sheet splitting, long text and safe formula-like strings
without generating million-row fixtures. Browser coverage also checks local-demo
workbooks and desktop/mobile export-action alignment. Run it with
`dotnet test api/tests/Kanbada.Api.Tests --filter FullyQualifiedName~ExportTests`.
To run the API suite without the optional million-card regression, use
`dotnet test api/tests/Kanbada.Api.Tests --filter 'FullyQualifiedName!~MillionCard'`.

`PaginationTests` covers page bounds, stable ordering, all filters, exact
title/label/ID search, invalid/stale/query-mismatched cursors, authorization,
structural renames, preserved positions and notification paging.
Its optional scale regression inserts **1,000,000 cards** into an isolated schema
and verifies metadata tracks zero cards, a 40-card response tracks only 40 cards
and stays below 32 KB, and detail reads/edits and totals still work.

`npm test` runs `scripts/check-api-browser.mjs` against the real API. It creates a unique development account and verifies sign-in, Help search/FAQ/Portuguese (59 articles, Jira/GitHub categories, SVG, recovery codes and read-only guidance), card persistence and history, PDF export, dark theme, member profile editing, drawer blur, full-width layouts, and creating a card through Scalar’s actual Send Request UI. The test reports browser exceptions. It intentionally leaves its test account and card in the development database; do not run against production.

`npm --prefix portal run test:jira` checks project settings, discovery dropdowns,
cron presets, persisted configuration, stale-version rejection and token secrecy.
It uses real API persistence with mocked Jira discovery responses, leaves its
development account/configuration disabled, and never contacts an external Jira.
This script is also included in `npm test`.
It also verifies the card editor's Jira link, new-tab attributes, desktop/mobile
visibility, and lookup-error retry behavior using mocked card-link responses.
Mapping UI checks save/reload multiple statuses, switch the outbound default, and
map a Jira identity to a registered workspace member.
They also verify automatic named-choice loading, read-only card controls and drag
prevention, and the assignment notification shown to the assigned member.

API regressions exercise locked-card PATCH/PUT rejection, read-only flag spoofing,
workspace deletion protection, worker updates, and unlocking after a direction
change. Assignment tests cover recipient privacy, dismissal isolation, unchanged
assignments, reassignment, and Jira-origin notification generation. Metadata tests
resolve saved choices outside the JQL and report inaccessible choices explicitly.
It also checks the frontend server-approval/revocation flow using mocked approvals
so the installation's network policy is not changed. `BrandingTests` verifies
real administrator authorization, exact-port matching and immediately effective
database approvals/revocations across service scopes.

The older feature scripts exercise explicit local mode and retain useful UI regression coverage:

```sh
cd portal
VITE_DATA_MODE=local npm run dev
```

In a second terminal, `npm --prefix portal run test:local` runs the legacy baseline. Focused scripts in `portal/scripts/check-*.mjs` cover workspaces, labels, themes, notifications, files, sharing, and dashboards. Stop the API-mode Vite process before binding a local-mode process to the same port. Legacy local tests are not evidence of server authorization correctness.

## Making changes

1. Locate the owning feature using [architecture](architecture.md).
2. Keep domain rules independent of HTTP, React, and persistence. Introduce focused services/hooks/components when responsibilities diverge, not a new generic abstraction for every helper.
3. Use descriptive names, typed inputs/callbacks, early validation, immutable frontend updates, async database I/O, EF LINQ queries and tracked updates, and centralized API errors. Comments should explain invariants and tradeoffs rather than narrate syntax.
4. Preserve membership checks, protected resources, server-generated history, and version checks. Never trust client-provided ownership or audit history.
5. Add regression tests for changed behavior and security boundaries. Use browser checks for visual/keyboard behavior; avoid tests that merely duplicate implementation details.
6. Update both EN/PT copy and the relevant documentation. Update OpenAPI metadata when transport headers or routes change.
7. Run the checks above. Review the diff for unrelated generated files or secrets before committing.

Formatting and linting are maintained checks, not a guarantee that any developer will understand every domain decision. Document new tradeoffs and keep feature boundaries reviewable.

The relational migration tests import populated v1 documents, verify credentials/files/relationships and zero JSON columns, and check rollback on inconsistent legacy data. They also upgrade duplicate label definitions, verify preservation of card memberships and surviving metadata, check workspace version advancement, reject new duplicates at the database layer, and permit transactional label-name swaps. API tests also exercise definition renames, cascading workspace deletion, and simultaneous writers. Run `dotnet ef migrations has-pending-model-changes --project api` after model changes.

`check-payload-browser.mjs` exercises the actual portal repository against PostgreSQL with 100 cards and a large banner. It requires a normal edit request below 1 KB, its canonical response below 2 KB, more than 99% request reduction relative to a snapshot, no PUT saves, and body-free 304 refreshes. The regular browser suite also asserts that UI saves use PATCH. API tests verify compact deltas, retained large fields, server audit history, malformed/stale changes, and atomic rollback.
