# Project synchronization with Jira

Each Kanbada project can have one Jira connection. The workspace owner opens
**Project options → Jira synchronization** to configure it. Other members cannot
read or change the connection, run jobs, or access its credentials. Local/demo
mode does not expose this feature.

The settings dialog groups connection details, field mappings, scheduling and run
status into separate panels. The save bar stays visible while scrolling. It follows
the portal's light/dark theme and stacks the panels and controls on smaller screens.

## Open an issue from its card

Open a synchronized card from the board or list. Its **Details** tab displays an
**Open in Jira** link and the saved issue key, opening Jira in a new tab. Existing
imports and cards exported to Jira are supported, including when synchronization
is disabled. Cards without a confirmed Jira issue show no link.

All workspace members can use this link; Jira still controls access to the issue
and may require sign-in. The member-only
`GET /api/workspaces/{id}/cards/{cardId}/jira` endpoint returns only keys and browser
URLs, never tokens or connection settings. Server context paths are preserved;
scoped Jira Cloud connections use `serverInfo` to resolve the browser URL instead
of linking to the API gateway. Lookup failures show a retry action in the editor.

## Connect

1. Choose **Data Center / Server** with a personal access token (Bearer
   authentication), or **Cloud** with the account email and API token (Basic
   authentication). The token needs permission to browse the selected issues and,
   for outbound synchronization, create/edit issues and execute transitions.
2. Enter the HTTPS base URL, retaining any Data Center context path, the Jira
   project key for new issues, and your JQL (including `ORDER BY` if desired).
   Classic Cloud API tokens use `https://your-site.atlassian.net`. Scoped Cloud
   tokens use `https://api.atlassian.com/ex/jira/YOUR_CLOUD_ID`; the administrator
   can approve `api.atlassian.com` in **Server access** as described below.
3. **Test connection and load mappings** checks credentials and JQL without
   writing issues. Select the issue type for new issues and map Kanbada statuses
   and Low/Medium/High priorities using Jira-name dropdowns. IDs are stored internally,
   not entered in the frontend. A Kanbada status can have several
   Jira statuses; choose one default per mapped Kanbada status. Unmapped values fail
   explicitly; they are never silently replaced by a default.
4. Choose the direction, schedule, time zone, and enable synchronization. Save,
   then optionally **Run saved configuration now**. Use **Refresh status** to
   inspect completion and errors. Unsaved form changes do not affect manual runs.

Jira's required custom fields are not supplied by this connector. Choose a
standard issue type whose required fields have defaults, and a workflow with an
available direct transition to each mapped status. A transition with required
fields or multiple possible transitions to the same status produces an explicit
error. Jira labels cannot contain whitespace; Kanbada labels must be at most 120
characters. The configured target project must allow the selected issue type.
Status discovery combines the target project's workflow with statuses observed in
JQL results, including other projects. Saved connections load choices automatically
when opened. Saved status/user mappings outside the current JQL are resolved by ID
to their current Jira names. Inaccessible choices are explicitly marked unavailable;
they are never silently replaced. Use **Test connection and load mappings** to retry
or reload choices after changing the server, credentials, project, or JQL.

## Status and assignee mappings

Under **Status mapping**, use **Add Jira status** for additional Jira states that
should appear as the same Kanbada status. Each Jira status belongs to one Kanbada
status within a connection. The **Default** radio selects the target when writing
that Kanbada status to Jira. If Jira is already in any state mapped to that Kanbada
status, no transition occurs. This preserves distinctions such as Jira Open and
Selected both appearing as Kanbada Backlog. Priorities remain one-to-one.

Under **Assignee mapping**, enable **Synchronize assignees from Jira** and map
Jira identities to registered workspace members. Existing connections default to
disabled so upgrades do not replace local assignments. Cloud uses `accountId`;
Data Center uses the user `key`, falling back to `name` only when Jira supplies no
key. The dropdown displays names for assignees from JQL results and saved mappings,
not the entire Jira directory. Display names and emails are never used as matching
keys. Pending invitees cannot
be mapped until they have joined the workspace.

Assignees flow only from Jira in **Jira to Kanbada** or **Bidirectional** mode.
The single mapped member replaces the card's current assignees. An unassigned Jira
issue clears the local assignees without warning. An unmapped Jira user (or a
mapping whose member has left) also clears them, but records an **Assignee mapping
warning** in the run/link details. Other mapped fields still synchronize. Fixing
the mapping resolves the assignment and warning on the next run even if no other
issue fields changed. Missing assignee data in a Jira response is an error, not an
instruction to clear assignments. Jira assignees are never written, including
during issue creation. In **Kanbada to Jira** mode local assignees are untouched.

The API's `syncAssignees` flag defaults to `false`. Mapping entries use
`kind: "status" | "priority" | "assignee"`, `kanbadaValue`, `jiraValue`, and
`isDefault` (defaults to `true` for compatibility with existing single-status
clients). For assignees, `kanbadaValue` is the member's registered user UUID.
For statuses, exactly one row per Kanbada status must have `isDefault: true`.

New assignments to registered members create a private notification in that
member's workspace notification area, including assignments made by Jira imports.
Unchanged assignments do not notify again, even on repeated sync runs. Notifications
are not backfilled for existing assignments.

## Read-only Jira-managed cards

Cards with a confirmed Jira link are entirely read-only for users when their
connection is set to **Jira to Kanbada**. This includes fields, assignments,
comments, checklists, attachment changes, moves and deletion. The board shows a
lock and disables dragging; the editor keeps history, downloads, and **Open in Jira**
available while disabling editing controls. Unlinked cards remain editable.

The API enforces the same rule for PATCH and PUT; removing or changing the
server-derived `readOnly` field cannot bypass it. A workspace containing locked
cards cannot be deleted. Pausing synchronization does not unlock them. The
workspace owner must change the connection direction to permit local edits.
Direction changes advance the workspace version so stale clients cannot write
under an outdated policy. The worker can still apply Jira updates.

## Direction, scope, and conflicts

| Direction | Behavior |
|---|---|
| Jira to Kanbada | Import matching unlinked Jira issues; overwrite synchronized card fields from Jira. Never write Jira. |
| Kanbada to Jira | Create issues for unlinked cards in this Kanbada project; update linked issues only while they match JQL. Never import unlinked Jira issues. |
| Bidirectional | Create missing counterparts and propagate whichever side changed since the last successful synchronization. |

Fields are title/summary, description, status, priority, labels and due date.
Other fields, including comments, attachments, buckets and swimlanes,
are not synchronized or erased. Imported labels are created as workspace label
definitions when needed. Label names are matched without regard to capitalization
or surrounding spaces; existing spelling and color are retained. Multiple Jira
variants become one Kanbada label and one association per card. Database uniqueness
also prevents concurrent imports from creating duplicate definitions.
Upgrading merges existing case/space variants while preserving every card's label
membership. It keeps a manually created definition when available, otherwise the
first definition in workspace order, retaining its spelling, color and completion
flag. Affected workspace versions are advanced so stale saves cannot undo the repair.

In bidirectional mode, when **both** sides changed, the original creation system
wins for the entire synchronized field set: Jira for imported cards, Kanbada for
exported cards. A change on only one side propagates regardless of origin.
Creation origin is persisted and does not change when the direction changes.
Existing unrelated cards/issues are not matched heuristically by title.
Optional inbound assignee mapping runs independently of these conflict hashes:
Jira remains authoritative for assignees even when Kanbada wins the other fields.

JQL is evaluated on every run, using paginated Cloud v3 enhanced search or Data
Center v2 search. Its scope applies to linked issues in both directions. New local
cards are created in the configured target project before Jira can evaluate the
JQL: make sure the query will include them. Search indexing delays can temporarily
exclude newly created issues. Queries returning more than 10000 issues fail
before synchronization instead of silently truncating results.

Neither system's deletions are propagated. A deleted/moved Kanbada card leaves a
link tombstone and is not recreated; a missing/inaccessible Jira issue, or one
outside the JQL, is left alone and reported. Archiving the Kanbada project pauses
scheduled/manual runs. Deleting the project removes its connector and links but
does not delete Jira issues. Disable synchronization and let an in-flight run
finish before deleting a project or making major configuration changes.

Cloud descriptions are projected from Atlassian Document Format to plain text.
Paragraphs, line breaks, mentions and link-card URLs are represented; media is
shown as a placeholder. Formatting is not round-tripped. An outbound change
replaces the description with plain-text paragraphs only if its text changed;
unrelated edits preserve the existing rich description. Data Center descriptions
are copied as text, including any wiki markup.

## Cron schedules

The UI builds presets for minutes, hours, days and months and also accepts a
five-field cron expression interpreted by Cronos in the selected IANA time zone.

| Example | Expression |
|---|---|
| Every 15 minutes | `*/15 * * * *` |
| Every 2 hours, on the hour | `0 */2 * * *` |
| Daily at 09:00 | `0 9 * * *` |
| On odd-numbered days, at midnight | `0 0 */2 * *` |
| Monthly on the first day at midnight | `0 0 1 * *` |
| January, April, July, October on the first day | `0 0 1 */3 *` |

Cron field steps follow calendar boundaries: `*/2` in the day field is not a
continuous 48-hour duration, and `*/15` in minutes restarts each hour. Dates that
do not exist (for example February 31) are skipped. Cronos supplies its timezone
and daylight-saving behavior. If both day-of-month and day-of-week are restricted,
Cronos requires both to match.

The worker checks for work every 10 seconds and processes connections sequentially.
Schedules are not a real-time SLA. Downtime coalesces missed occurrences into one
run; the next occurrence is calculated after completion. Each run has a 15-minute
limit and each HTTP request a 45-second timeout. Failed items are retried on the
next scheduled/manual run; one bad item does not stop the remaining items.

## Worker and deployment

`worker/Kanbada.Worker.csproj` is a separate executable process. It references the
API assembly for the shared EF model and connector implementation, not HTTP
endpoints. The root Compose file builds and starts it alongside the API:

```sh
podman compose up --build --force-recreate -d --wait --wait-timeout 600
podman compose logs -f worker
```

For private/Data Center hosts, enter the full HTTPS Jira base URL and open
**Server access** in the Jira synchronization dialog. A platform administrator
selects **Approve this Jira server**, checks the displayed hostname/port, and
confirms. The approval is stored in PostgreSQL and applies immediately to both the
API and worker. **No environment variables, files, or container restarts are
required.** Workspace owners without platform administration ask an administrator
to approve the server through the same panel, then finish their own connection.

The panel lists approved hosts and lets administrators revoke them. Approval is
platform-wide and limited to an exact HTTPS authority (hostname plus non-default
port). Approve only trusted Jira destinations. Revocation blocks subsequent
requests, including worker runs; a request already in flight can finish. Classic
`*.atlassian.net` Cloud sites on port 443 remain allowed automatically.

Existing `JIRA_ALLOWED_HOSTS` / `Jira:AllowedHosts` deployment policies remain
supported for backward compatibility and appear read-only in the panel. Removing
a database approval cannot override an explicit deployment-policy or built-in
Cloud allowance. New connections should use the frontend approval workflow. Redirects
are disabled so credentials cannot follow them to another host. Trust only
administrator-controlled destinations, restrict container egress at the network
layer, and install the enterprise CA in both images if the Jira server uses one.
TLS validation is never disabled.

The API and worker must use the same PostgreSQL database and shared
`.data-protection` directory. Tokens are encrypted with a separate
`Kanbada.Jira` Data Protection application discriminator and are never returned
by settings endpoints or placed in workspace snapshots/exports. A blank token on
save preserves the saved value; changing the host/account requires a new token.
Once links exist, the Jira instance, edition and target project cannot change,
to prevent writing existing numeric issue IDs into an unrelated installation.
Protect and back up the key directory as well as PostgreSQL; encryption keys
still need platform-level at-rest protection in production.

Apply the `JiraSynchronization` EF migration before starting production
processes. Development API startup applies it; the worker waits for migrations
and never applies them itself. To run the worker outside Compose, configure
`ConnectionStrings__Postgres` and `Jira__KeyDirectory` (pointing to
`api/.data-protection`), then run `dotnet run --project worker`.

PostgreSQL session advisory locks prevent concurrent runs for the same
connection, including across worker replicas. Each incoming card change, link
checkpoint, history entry and workspace version increment commits atomically.
Workspace optimistic concurrency protects against overwriting a simultaneous
portal save. Remote updates cannot be part of the PostgreSQL transaction; a user
editing Jira after the final remote read can still race with an outbound write.
The connector verifies returned field values and reports mismatches rather than
claiming success. There is no cross-system transactional guarantee.

## Interrupted creation and errors

Jira does not offer an idempotency key for issue creation. The worker persists a
pending creation **before** sending the request. If its result is lost, it does
not automatically POST again. Importing unlinked issues is also paused until the
ambiguity is resolved, avoiding a second local copy of a remotely created issue.

In the synchronization status, inspect Jira and enter the created issue's
**numeric ID**, not its display key, then select **Resolve interrupted creation**.
After confirming the association, request another run. If no issue was actually
created, create the corresponding issue manually, then attach its numeric ID.
Never attach an unrelated issue: subsequent runs may overwrite its mapped fields
according to the connection's direction and creation origin.

The UI shows the last run summary and up to 100 problematic links. Authentication,
rate-limit, mapping, transition, timeout and concurrency failures are explicit.
HTTP writes are not automatically retried within a run. Tokens, authorization
headers, request bodies and remote error bodies are not logged.
