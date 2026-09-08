# Authentication and authorization

## Email accounts and sessions

Email registration requires a name, valid email address, and a password of 12–200 characters. Passwords are hashed with Microsoft's ASP.NET Core Identity `PasswordHasher`; plaintext passwords are not persisted. Local sign-in and registration use the ASP.NET Core rate limiter.

The API signs a protected `kanbada_session` cookie using ASP.NET Core cookie authentication and Data Protection. It is HttpOnly, SameSite=Lax, and secure for HTTPS requests. Sessions have a fixed eight-hour lifetime. `SessionEvents` checks the session row, user, and expiry on authenticated requests. Logout removes that row and clears the cookie, so a replayed revoked session is rejected.

The portal refreshes session state on focus, once a minute, across tabs, and after a 401. It stores a non-secret cross-tab notification, not authentication tokens. Profile photos and workspace membership display details remain in workspace state; changing your own display name also updates your account's session display name. Login email is read-only in the API-backed profile form.

There is currently no password reset, verified-email delivery, MFA, or account recovery UI. Add those flows before making password accounts subject to recovery or verification requirements. Do not represent browser-only local mode as secure login.

## Google

Create a Google OAuth web application and configure the public callback URI:

```text
http://localhost:4173/api/auth/google/callback
```

For production use the public HTTPS origin. Configure `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` in `api/appsettings.Local.json` for local development or environment variables/your secret manager for deployment. Do not commit credentials.

## Microsoft

Register an application in Microsoft Entra, select the supported account audience appropriate for your users, and configure a Web redirect URI:

```text
http://localhost:4173/api/auth/microsoft/callback
```

Configure `Authentication:Microsoft:ClientId` and `Authentication:Microsoft:ClientSecret`. The integration uses Microsoft's `AddMicrosoftAccount` middleware. A tenant-restricted enterprise OIDC deployment may require different registration/middleware configuration; it is not assumed here.

Provider buttons are enabled only when both values are configured. Start sign-in from the portal origin so callback cookies and redirects use the same host. Each provider subject creates/identifies its own account. Matching email text does not automatically link password, Google, and Microsoft identities.

See Microsoft's [external authentication guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/?view=aspnetcore-10.0).

## Workspace permissions

Every read and write requires membership. Owners can change workspace settings, invite/remove members, and delete non-personal workspaces. Members can collaborate on cards and workflows and edit their own profile. Personal workspaces and My activities cannot be deleted; My activities cannot be archived or renamed.

Inviting a member creates a pending record and a random link token. The owner copies the invitation URL and sends it to the intended recipient. Kanbada does not send emails. Acceptance requires a signed-in account matching the invited email. Removing membership revokes ordinary workspace access immediately.

## Sharing

A card link always requires authentication. `members` links additionally require current workspace membership. `signed-in` links allow any authenticated user who holds the token to read that card; removing workspace membership does not revoke an independently held signed-in link. Revoke or replace the link to remove that access.

Links use cryptographically random tokens and optional seven/thirty-day expiry. Replacing a link invalidates the old token. Shared responses expose one card and its display context, not the workspace's other cards or member collection. Shared files must be attached to that card. There is no shared-card edit endpoint.

## Request boundary

Mutations require `X-Kanbada-Request: 1`, and supplied Origin values are checked. There is no credentialed cross-origin CORS policy. Browser HTML forms cannot supply this custom header, and other origins cannot preflight it successfully. Multipart uploads use this same request boundary rather than ASP.NET antiforgery form tokens. Preserve these assumptions if changing hosting or adding CORS.

Responses include `nosniff`, `same-origin` referrer policy, and `no-store`. Never log passwords, session cookies, connection strings, or invitation/share tokens. Log correlation IDs and operation context instead.
