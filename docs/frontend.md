# Frontend

## Stack and entry point

React 19, strict TypeScript, Vite 6, Lucide icons, and CSS. `src/main.tsx` mounts language, theme, authentication, and shared-link boundaries before the app. `src/app/App.tsx` coordinates the active workspace and navigation.

`CardDrawer` owns the Details/History tabs, card form, and conversation draft. It receives typed save/delete commands. `BoardCard`, `BoardToolbar`, `CalendarView`, `TaskList`, `Sidebar`, and `WorkspaceDialogs` separate rendering by responsibility. Existing feature components cover dashboard, workflow configuration, workspace appearance, membership invitations, and help.

## State and I/O

`domain/models.ts` defines the workspace snapshot and attachment metadata. `domain/projectRules.ts` enforces protected project behavior in the UI. `domain/cardHistory.ts` supports local change previews; the API independently regenerates saved history.

`app/hooks/useWorkspaceLocation.ts` loads workspaces, resolves `?workspace=...&card=...`, handles browser history, and listens for account-scoped local storage changes. `useWorkspaceSave.ts` coordinates writes, reports errors, and replaces state only after a successful save. The app pauses background refresh while editing and ignores stale in-flight refresh results after effect cleanup.

`infrastructure/workspaceRepository.ts` declares the common asynchronous repository contract. The API implementation is the default. A save returns the canonical workspace including its new version; adapters never mutate the input. Use `structuredClone` or immutable updates when editing drafts. Components should not call `fetch`, interpret HTTP errors, or write workspace data directly to browser storage.

`apiClient.ts` owns relative API URLs, credentials, the required mutation header, Problem Details parsing, and session-expiry events. Uploads use `FormData`; the browser supplies its boundary. File downloads revoke temporary object URLs after use.

`VITE_DATA_MODE=local` selects explicit browser-only behavior for legacy checks. It uses isolated account-prefixed localStorage and IndexedDB. It is not real authentication, does not call PostgreSQL, and must not be enabled in a production build. Existing local data is retained; it is not automatically imported into API accounts.

## Navigation and authorization

- `?workspace={id}&card={UPPERCASE_CARD_ID}` opens a card drawer.
- `?share={token}` opens a read-only shared card after authentication.
- `?invite={token}` opens invitation acceptance after authentication.
- My tasks includes the signed-in member's tasks across active projects in the current workspace.
- Personal workspace IDs appear as `studio` for their owner; the server resolves this to the actual UUID.

Hiding a delete button is only a UI affordance. The API independently checks membership, ownership, and protected resources.

## Theme, layout, and accessibility

Edit `styles/styles.css` for base styles and `styles/dark-overrides.css` for explicit dark adjustments. `scripts/generate-dark-theme.mjs` creates `styles/dark.css` during builds. Do not edit that generated file. The theme provider supports light, dark, and the system preference.

All main application pages use the full available width, including dashboards, lists, project directories, Help, wide displays, and a collapsed sidebar. Board columns retain a 230px minimum and scroll horizontally only when they cannot fit.

The card drawer inherits the modal overlay's background blur. Preserve dialog labels, focus trapping, Escape handling, reduced-motion behavior, keyboard controls, and responsive layouts when changing it. Accessibility regressions need browser verification; linting alone is insufficient.

## Translation and Help

`shared/i18n.tsx` provides `useI18n`; `shared/pt-PT.json` contains Portuguese translations. Keep persisted IDs and workflow references independent of translated display text. Custom user content is not machine-translated.

`features/help/helpContent.ts` contains 38 paired EN/PT FAQ entries. The Help view searches accent-insensitively and filters by category. Update both language versions when functionality changes.

## Dashboards and exports

Dashboard metrics are derived from the current workspace snapshot and project/bucket/swimlane filters. Completed statuses are configurable. Empty due dates are not overdue. Archived projects are omitted from active work views.

`features/dashboard/dashboardPdf.ts` renders PDF exports with jsPDF and AutoTable and is loaded on demand. PDF generation stays in the browser; it does not upload dashboard content to an external service. The API also provides filtered metrics and a workspace JSON export.

## Adding a feature

Add models/rules to `domain` only if they are shared business concepts. Put the view in the owning feature folder. Pass explicit state and commands from the shell, or add a focused hook for reusable lifecycle behavior. Add network behavior to the infrastructure adapter and matching API feature. Avoid expanding `App.tsx` with another full form or request implementation.

## Network payloads

`stateChanges.ts` compares editable fields in the exact loaded state and draft, emitting add/remove/replace operations. Server-controlled history, ownership, member identity, and version are excluded. Array additions such as notifications and history do not resend an entire collection; clearing a collection sends an empty-array replacement. The repository sends PATCH with the loaded version and applies the server's canonical delta to a cloned base. It does not follow a successful save with a full GET.

Account/workspace-keyed memory snapshots support conditional background GETs. An unchanged authorized workspace returns 304 and reuses the cached clone. Initial loads and changed refreshes still download a complete workspace; lazy history loading and paginated card reads are not implemented. The server currently uses the workspace aggregate internally for cross-resource validation, even though it receives and returns compact payloads.
