# Platform branding

Branding is shared by the entire installation, including the sign-in/enrollment
pages, navigation logo, shared-card/invitation headers and browser title/favicon.
It does not replace individual workspace icons/banners or semantic card
status/priority colors. API documentation and authenticator issuer names remain
Kanbada; those identify the underlying product rather than a workspace brand.

## Configure administrators

First register the intended administrator account normally and complete its avatar
and authenticator enrollment. Then add its existing email address to the ignored
root `.env`:

```dotenv
PLATFORM_ADMIN_EMAILS=admin@example.com,second-admin@example.com
```

Recreate the API to apply the environment configuration:

```sh
podman compose up --force-recreate -d api
```

Outside Compose, use `Platform__AdminEmails` or the `Platform:AdminEmails`
configuration key. No administrator is assigned automatically. Workspace owners
cannot manage platform branding unless separately configured as platform admins.

At startup the API resolves each email to exactly one **existing account ID**.
Missing or ambiguous accounts cause startup to fail explicitly. This prevents a
future signup from claiming administrator access by registering a configured email.
Operators must verify that the existing account belongs to the intended person;
email registration alone is not proof of email ownership. Administrator identities
are not exposed in the public branding response. Removal from configuration takes
effect on API restart. The worker needs no administrator configuration.

## Customize

Open **Settings → Platform appearance** as a configured administrator:

- Set the platform name (1–60 characters).
- Upload a PNG, JPG, GIF, WebP or SVG logo up to 2 MB, or remove it to restore the
  original symbol. SVG uploads are decoded as images (not inserted into the page)
  and converted in the browser to transparent PNGs with a 1024-pixel longest edge.
  Aspect ratio is preserved; the converted image must also fit within 2 MB.
  The API accepts only raster data URIs, not raw SVG or remote image URLs.
  Turn off **Show name beside logo** to display only a centered logo. The name
  remains required for the browser title and accessible identity. The setting
  applies to navigation, sign-in, enrollment and shared/invitation branding.
  Upload a separate **Collapsed sidebar logo** for the compact navigation icon.
  Both uploads support the same formats and limits. Without a collapsed upload,
  the sidebar uses the main logo. Expanded navigation, mobile navigation and other
  screens retain the main logo; removing a collapsed logo restores that fallback.
- Choose the primary action color and accent color.
- Choose background, panel, text and border colors independently for light and
  dark mode, plus sidebar background and text colors. Optional colors have an
  **Automatic** option to derive them from the theme. Color pickers also accept
  hexadecimal values. Foregrounds are adjusted for readability; button text
  contrast is selected automatically. Preview both modes before publishing.
- Choose the default, system, DM Sans or Manrope font family, text size from
  90–120%, and corner rounding from 0–16. Shared theme tokens apply these choices
  throughout navigation, views, dialogs, forms and authentication screens.
  Status, priority, label, avatar and other user-defined content colors are preserved.
- Board, List and Calendar retain their underline-style active tabs. Selected
  sidebar projects use a subtle sidebar-derived background with readable text,
  rather than a solid accent fill.
- Choose the platform's default light, dark or system theme. Existing explicit
  browser preferences still win; the default applies to visitors without one.

The live preview is local to the dialog; unsaved changes never affect other users.
**Save platform appearance** publishes changes to the installation. Other open
tabs refresh public branding every minute; a reload applies it immediately. Settings
use a version token to reject stale updates rather than overwrite another admin's
changes. **Reset to defaults** restores the original values in the draft and
requires saving before it changes the platform.

Branding is stored as one versioned relational row in `platform_branding`.
The logo is persisted as a validated raster data URI, so database backups include
it and no additional volume is required. Public reads use ETags; no secrets or
permissions are in the public document. A failed initial branding load displays
an explicit retry screen rather than silently presenting the wrong identity.

Apply the `PlatformBranding` and `ExpandedPlatformTheme` EF migrations before production deployment.
The expanded migration preserves existing branding, adds nullable automatic
colors, and defaults to the original font family, 100% text size and rounding 8.
`BrandingLogoNameVisibility` adds `showName`, defaulting to true for existing
installations and API requests that omit it.
`CollapsedSidebarLogo` adds an optional `collapsedLogo` raster data URI without
changing the existing main logo.
Development API startup applies it automatically. The browser-only demo keeps
the default identity and does not expose platform administration.
