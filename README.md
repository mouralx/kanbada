# Kanbada

Kanbada brings workspaces, projects, Kanban boards, cards, files, and dashboards together. The React portal connects to an ASP.NET Core 10 API backed by PostgreSQL 17 through Entity Framework Core 10.

## Requirements

- .NET 10 SDK (`global.json` selects an installed 10.0 feature band).
- Node.js 22.12 or newer and npm.
- Docker with Compose, or Podman with a Compose provider. On macOS/Windows, start the container engine's virtual machine first.

## Start locally

Run the following from the repository root:

```sh
node scripts/setup-local.mjs
npm --prefix portal ci
dotnet tool restore
dotnet restore api/Kanbada.Api.csproj --locked-mode
docker compose -f api/compose.yaml up -d --wait
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

Replace `docker compose` with `podman compose`. On macOS, run `podman machine start` first. If a `kanbada-postgres-api` container already exists from an earlier setup, start it with `podman start kanbada-postgres-api` rather than creating a second container with the same name. See [troubleshooting](docs/operations.md#troubleshooting).

### Stop

Press Ctrl+C in the API and portal terminals. Stop PostgreSQL without removing its data:

```sh
docker compose -f api/compose.yaml stop
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

Start with the [documentation index](docs/README.md) for architecture, source ownership, API contracts, database migrations, authentication, deployment, and contribution guidance.

- `portal/`: frontend source and browser checks.
- `api/`: backend source, migrations, and integration tests.
- `docs/`: technical documentation.
- `scripts/`: repository setup utilities.

Existing installations: follow the [EF migration procedure](docs/database.md#migrations-and-existing-data) before upgrading the database. The relational conversion preserves existing data and the portal API contract.
