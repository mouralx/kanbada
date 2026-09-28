# Kanbada

Kanbada brings workspaces, projects, Kanban boards, cards, files, and dashboards together. The React portal connects to an ASP.NET Core 10 API backed by PostgreSQL 17 through Entity Framework Core 10.

## Requirements

- .NET 10 SDK (`global.json` selects an installed 10.0 feature band).
- Node.js 22.12 or newer and npm.
- Docker with Compose, or Podman with a Compose provider. On macOS/Windows, start the container engine's virtual machine first.

## Start locally

### Entire solution with Compose

From the repository root:

```sh
node scripts/setup-local.mjs
node scripts/build-images.mjs podman
podman compose up --force-recreate -d --wait --wait-timeout 600
```

Open **[Kanbada](http://localhost:4173)**. Compose starts PostgreSQL, the API
(with development migrations), the Jira synchronization worker, and the portal development server. Node.js
and a running container engine with Compose are needed on the host; .NET and
portal dependencies are installed when building the images. The API uses a
multi-stage build with an ASP.NET runtime image; the portal image runs Vite.
After source changes, rerun `node scripts/build-images.mjs podman` before restarting
the stack. Docker users can replace `podman` with `docker`.
This configuration is for local development, not production.

Stop the solution with `podman compose stop`; inspect logs with
`podman compose logs -f`. Database data and API Data Protection keys persist
(keys remain in `api/.data-protection` on the host). The root Compose file can
also run PostgreSQL by itself for host development.
Stop any host API/portal processes before starting the full solution.

### API and portal on the host

Run the following from the repository root:

```sh
node scripts/setup-local.mjs
npm --prefix portal ci
dotnet tool restore
dotnet restore api/Kanbada.Api.csproj --locked-mode
docker compose up -d --wait postgres
```

The setup script generates a random database password and creates ignored local configuration files. It preserves existing configuration. PostgreSQL stores data in a persistent volume.

Start the API in one terminal:

```sh
cd api
dotnet run
```

Start the portal in another terminal:

```sh
cd portal
npm run dev
```

Open **[Kanbada](http://localhost:4173)**. Select **Continue with email → New here? Create an account**. Passwords must contain at least 12 characters. Each new account receives **My Workspace** and **My activities**.

- **[Scalar API reference](http://localhost:4173/api/scalar)** — interactive documentation through the portal proxy, sharing the browser's authenticated session.
In Scalar, open **Cards → Create a card**, set `id` to `studio`, and send `{ "title": "My first card" }`. See the [step-by-step API guide](docs/api.md#create-your-first-card-in-scalar).

- **[Scalar directly on the API](http://127.0.0.1:5180/api/scalar)** — sign in separately on this origin to try protected endpoints.
- **[OpenAPI JSON](http://127.0.0.1:5180/api/openapi.json)**.
- **[Readiness check](http://127.0.0.1:5180/api/health/ready)**.

Google and Microsoft sign-in become available after configuring application credentials and callback URLs. See [authentication](docs/authentication.md). Email sign-in works without those credentials.

### Podman

The full-solution instructions above use Podman; Docker users can substitute `docker compose`. For the host-development instructions, replace `docker compose` with `podman compose`. On macOS, run `podman machine start` first if the machine is stopped. If a manually created `kanbada-postgres-api` container already exists from an earlier setup, resolve the name conflict before starting Compose; do not delete its database volume. See [troubleshooting](docs/operations.md#troubleshooting).

### Stop

Press Ctrl+C in the API and portal terminals. Stop PostgreSQL without removing its data:

```sh
docker compose stop postgres
```

## Check your changes

```sh
npm --prefix portal run check
npm --prefix portal run build
```

With PostgreSQL running:

```sh
cd api
dotnet test tests/Kanbada.Api.Tests/Kanbada.Api.Tests.csproj
```

With the API and portal also running:

```sh
npm --prefix portal test
```

API tests create and remove isolated schemas. Browser tests create uniquely named accounts in the configured API database; use a development database. See [testing](docs/testing.md) for details.

## Developer documentation

Platform administrators can customize the instance name, logo, colors and default
theme in **Settings → Platform appearance**. Configure existing administrator
accounts with `PLATFORM_ADMIN_EMAILS`; see [platform branding](docs/branding.md).

Project owners can configure **Project options → Jira synchronization** for Jira
Cloud or Data Center, one-way or bidirectional synchronization, and cron schedules.
See the [Jira setup and worker guide](docs/jira.md) for host approval, field mappings,
conflict rules and deployment requirements.

Start with the [documentation index](docs/README.md) for architecture, source ownership, API contracts, database migrations, authentication, deployment, and contribution guidance.

- `portal/`: frontend source and browser checks.
- `api/`: backend source, migrations, and integration tests.
- `worker/`: independently deployed Jira synchronization process.
- `docs/`: technical documentation.
- `scripts/`: repository setup utilities.

Existing installations: follow the [EF migration procedure](docs/database.md#migrations-and-existing-data) before upgrading the database. The relational conversion preserves existing data and the portal API contract.
