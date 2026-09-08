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

With API and portal running at the documented ports:

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

## Browser coverage

`npm test` runs `scripts/check-api-browser.mjs` against the real API. It creates a unique development account and verifies sign-in, Help search/FAQ/Portuguese, card persistence and history, PDF export, dark theme, member profile editing, drawer blur, full-width layouts, and creating a card through Scalar’s actual Send Request UI. The test reports browser exceptions. It intentionally leaves its test account and card in the development database; do not run against production.

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

The relational migration tests import populated v1 documents, verify credentials/files/relationships and zero JSON columns, and check rollback on inconsistent legacy data. API tests also exercise definition renames, cascading workspace deletion, and simultaneous writers. Run `dotnet ef migrations has-pending-model-changes --project api` after model changes.

`check-payload-browser.mjs` exercises the actual portal repository against PostgreSQL with 100 cards and a large banner. It requires a normal edit request below 1 KB, its canonical response below 2 KB, more than 99% request reduction relative to a snapshot, no PUT saves, and body-free 304 refreshes. The regular browser suite also asserts that UI saves use PATCH. API tests verify compact deltas, retained large fields, server audit history, malformed/stale changes, and atomic rollback.
