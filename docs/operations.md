# Configuration and operations

## Local configuration

Run `node scripts/setup-local.mjs` from the root. It writes `api/.postgres.env` and `api/appsettings.Local.json` with restrictive file permissions where supported, without printing or overwriting existing credentials. An example with placeholders is in `api/appsettings.Example.json`.

Configuration loads ASP.NET Core defaults, optional `appsettings.Local.json`, then environment variables. Environment variables override local JSON. Restart the API after changing local settings.

- `ConnectionStrings__Postgres`: required Npgsql connection string.
- `PortalOrigin`: public portal origin, default `http://localhost:4173`. HTTPS is required outside localhost/127.0.0.1.
- `Authentication__Google__ClientId` / `Authentication__Google__ClientSecret`.
- `Authentication__Microsoft__ClientId` / `Authentication__Microsoft__ClientSecret`.
- `ReverseProxy__KnownProxies__0` (and subsequent numeric entries): explicitly trusted proxy IPs for `X-Forwarded-For` and `X-Forwarded-Proto`; forwarding is disabled without this list. Preserve the public Host header at the proxy.
- `ASPNETCORE_ENVIRONMENT`: Development locally, Production when deployed.
- `ASPNETCORE_URLS`: overrides the listening URL when no explicit URL argument is supplied.

Vite listens on port 4173 with `strictPort` enabled: a port conflict fails clearly rather than silently starting on a different origin. Its `/api` proxy points to `127.0.0.1:5180`. Change both proxy/origin settings deliberately if using different ports. Always open `localhost:4173` for the documented OAuth flow; localhost and 127.0.0.1 are different cookie hosts.

## Build and deployment

```sh
npm --prefix portal ci
npm --prefix portal run check
npm --prefix portal run build
dotnet publish api/Kanbada.Api.csproj -c Release -o artifacts/api
```

Serve `portal/dist` as static files with fallback to `index.html` for frontend navigation. Route `/api/*` to the published API; never send API 404s through the frontend fallback. The API currently does not serve portal assets itself. Run the API with its content root set to the published API directory. Configure the production PostgreSQL connection and public HTTPS `PortalOrigin` via your secret/configuration service.

Use HTTPS externally and ensure the API sees the public host and HTTPS scheme for secure cookies and OAuth redirects. If terminating TLS at a proxy, configure trusted forwarded headers explicitly; do not trust forwarded headers from arbitrary clients. See the source configuration for supported proxy settings.

The `.data-protection` directory under the API content root holds keys used to encrypt cookies and authenticator secrets. Persist and share this directory when running replicas, protect it using the deployment platform's filesystem/secret controls, and configure a platform-appropriate at-rest protector before production. Deleting keys invalidates existing protected cookies and makes stored authenticator secrets unreadable; recovery codes can still be used to sign in. Restore the key backup to recover authenticator access. The local implementation persists keys but does not itself configure certificate/Key Vault encryption.

Keep PostgreSQL private. The local Compose port is bound to 127.0.0.1. Use TLS and a restricted database role in deployment. Apply EF migrations with a separate migration role before production startup; the runtime role does not need DDL rights. See [database migrations](database.md).

Configure reverse-proxy body limits consistently with the API's 32 MiB request limit and 25 MiB per-file upload limit. IP rate limiting is process-local and targets authentication; second-factor verification also has a database-backed account lockout shared across replicas; use gateway limits/distributed policy if replicas or abuse volume require it. Review capacity for workspace snapshot response sizes and in-database file storage before scaling.

## Health and logs

`/api/health/ready` returns success only when the PostgreSQL health check succeeds. Use it for readiness. `/api/health` provides a small connectivity response. ASP.NET Core logs go to standard output. Unexpected exceptions include method/path context; responses include a trace ID. Retain logs without secret values and configure platform log retention.

Scalar `/api/scalar` and `/api/openapi.json` are available in all environments. Apply your deployment access policy to both paths if they should be private.

## Backups

Back up PostgreSQL with the tools and retention policy for your environment. Both relational business records and file bytes are in the database. Back up Data Protection keys separately and securely: they are required to recover authenticator secrets and existing sessions. Test restoration into a separate database, validate schema versions, and check account/card/file access before relying on a backup.

`docker compose -f api/compose.yaml stop` preserves local data. Do not remove the named database volume to troubleshoot an application bug.

## Troubleshooting

- **Port 4173/5180 is busy:** stop the older Kanbada process or choose consistent alternative portal, API, proxy, and provider callback settings.
- **PostgreSQL connection refused:** start the container engine/VM, start the database, and check its health. With Podman on macOS, `podman machine start` is required after reboot.
- **Container name already exists:** an earlier database may be running as `kanbada-postgres-api`. Inspect/start it instead of deleting it or its volume. Existing installations may have a manually created container rather than Compose ownership labels.
- **Database password authentication failed:** compare local configuration with the credentials used to initialize the persistent database. Changing `.postgres.env` does not reset an existing PostgreSQL user's password. Restore the matching configuration or rotate the database password intentionally.
- **Portal cannot connect:** verify `http://127.0.0.1:5180/api/health/ready`, then `/api/health/ready` through Vite. API mode is the default; missing API connectivity is not silently replaced with local data.
- **403 on writes:** confirm `X-Kanbada-Request: 1`, the public Origin, and `PortalOrigin`. Do not remove the request-security middleware to bypass configuration mistakes.
- **409 on save:** another save won the workspace version race. Reload and reconcile changes; never auto-retry an old snapshot with a new version.
- **Google/Microsoft button disabled:** both client ID and secret must be configured, followed by an API restart. Verify the registered callback URI exactly matches the public origin.
- **Scalar can read but cannot write:** sign in on the same origin, include the mutation header, and set `If-Match` for workspace saves. The latest workspace JSON must contain all collections.
- **Old cards missing after API sign-in:** browser-only data and API accounts are separate stores. No automatic migration/import is performed.
- **Dark CSS changes disappear:** edit base CSS or `dark-overrides.css`; generated `dark.css` is rebuilt.

## Authenticator-based two-factor authentication

Every password and external-provider account must enroll an authenticator before workspace access, including accounts created before this feature. New registrations first require a profile photo, then open authenticator setup. A Gravatar lookup for the account email offers an explicit choice when a photo exists; users can always upload their own image. Gravatar outages do not prevent uploading. Every subsequent login requires a code. No account can disable 2FA. Setup requires the current password for password accounts, a locally rendered QR code (or manual key), and a confirming six-digit TOTP code. External accounts authenticate through their provider instead of confirming a local password. Microsoft Authenticator supports this as an **Other account**. New Google/Microsoft identities also enroll a Kanbada authenticator. Subsequent provider logins require a Kanbada authenticator or recovery code before workspace access. This feature does not add push approvals or link identities by email. Local demo mode does not offer production security settings.

Apply the `AuthenticatorTwoFactor`, `RequireTwoFactorForNewAccounts`, and `EnforceTwoFactorForAllAccounts` EF migrations before starting an updated production API. Development startup applies it automatically. Existing accounts without an authenticator are restricted to enrollment; there is no per-account opt-out flag. Existing sessions for accounts with an authenticator are re-verified after the mandatory-enrollment migration. No email service is required.

Pending enrollment expires in ten minutes. Codes use SHA-1, six digits and 30-second steps, accepting one adjacent step in either direction for clock skew. Keep server clocks synchronized. A consumed time step cannot be reused; wait for a fresh code after enrollment or another successful verification. Five failed password/code confirmations or second-factor verifications lock that account's second-factor operations for five minutes. API IP rate limits also apply.

Enrollment, recovery-code regeneration, and password changes revoke other sessions while retaining the session making the change. Ten random, single-use recovery codes are displayed once and can be downloaded; only their user-bound SHA-256 hashes are stored. Regeneration invalidates all previous recovery codes. Regenerating recovery codes requires the current password for password accounts and an unused authenticator or recovery code. Losing both the authenticator and all recovery codes has no self-service bypass.

Do not log authentication request bodies, QR/setup responses, or recovery-code responses. They are marked `Cache-Control: no-store`. Protect and persist the Data Protection key directory as described above; database encryption of authenticator secrets does not replace securing those keys.

Password accounts can use **Profile → Change password**. The server requires the current password, a new password of 12–200 characters, and an unused authenticator or recovery code. Verification, code consumption, password hashing, and revocation of other sessions are atomic. The portal also asks to confirm the new password. Google/Microsoft account passwords are managed by their provider. There is no unauthenticated password-reset or email recovery bypass.
