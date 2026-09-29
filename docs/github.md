# GitHub Projects synchronization

Workspace owners can open **Project options → GitHub synchronization** to connect
a Kanbada project to a GitHub **Projects v2** project. Organization and user-owned
projects, github.com, and Projects-v2-capable GitHub Enterprise Server installations
are supported. Classic projects are not supported.

Each Kanbada project supports **one connector: Jira or GitHub**, even when paused.
The API serializes connector creation on the project row, so concurrent saves
cannot configure both providers. A GitHub project cannot be connected to two
Kanbada projects in the same workspace. Use a separate Kanbada project for a
different remote project; changing the server, remote project or outbound
repository identity is blocked once items are linked. An inbound connection
without a repository can select its first outbound repository later.

## Configure

1. Enter `https://github.com` or your Enterprise Server HTTPS origin without an
   API path. Enter the project owner, owner type and project number from its URL.
   For `https://github.com/orgs/example/projects/12`, use organization `example`,
   project `12`. User projects use `/users/OWNER/projects/NUMBER`.
2. For outbound or bidirectional sync, enter the repository for **new issues** in
   `owner/repository` format. Existing project items may belong to other
   repositories; their repository does not change. Inbound-only connections can
   leave the repository blank.
3. Supply an owner-provided access token. The token needs project and repository
   access for everything it should synchronize. Classic PATs use `read:project`
   for reads, `project` for writes, and appropriate repository scopes for
   private content. Fine-grained tokens, where supported by the selected server,
   need the corresponding Projects, Issues and Pull requests permissions and
   repository access. Organization approval/SSO policies still apply.
4. **Test connection and load mappings** performs read-only GraphQL queries.
   Select a single-select status field, optionally another single-select field
   for priority, and an optional date field for due dates. Missing fields and
   unsupported Projects APIs fail explicitly. Test confirms read access; it does
   not prove mutation permissions by making test writes.
5. Map GitHub options to Kanbada statuses. Several GitHub options can share a
   Kanbada value; choose one **Default outbound** option for each mapped local
   value. Map **Empty GitHub field** explicitly when empty values are valid.
   If priority sync is enabled, map Low, Medium and High. Unmapped values produce
   errors rather than guessed defaults.
6. Optionally synchronize labels and inbound assignees. Find users by GitHub
   login, then select registered workspace members. Stable GitHub user node IDs,
   not display names or emails, are persisted as mapping keys.
7. Choose direction, five-field cron expression and IANA time zone, enable, and
   save. The default is every 15 minutes. **Run saved settings** queues a worker
   job, never runs in the browser or request handler. Unsaved changes are ignored.
   The open dialog refreshes run status every five seconds.

**Pause synchronization** works without contacting GitHub, even when the token
has expired or the server is unavailable. In-flight requests may finish.

Blank token input preserves the saved token only for the same HTTPS server.
Tokens are encrypted with a separate GitHub Data Protection application/purpose;
neither plaintext nor encrypted tokens appear in settings responses, cards,
workspace exports or member-visible links.

## Direction and field behavior

| Direction | Behavior |
| --- | --- |
| GitHub to Kanbada | Import issues, pull requests and draft items; update linked cards without remote writes. |
| Kanbada to GitHub | Create repository issues for unlinked cards, add them to the project, and update linked items; do not import unrelated project items. |
| Bidirectional | Create missing counterparts and propagate changes. If both sides changed, the original creation system wins. |

Titles, Markdown descriptions and project status synchronize. Priority and due
date synchronize only when configured; other local values are preserved.
Labels synchronize for issues and pull requests when enabled. Missing repository
labels are created before assigning them. Existing workspace labels are reused
case-insensitively without changing their spelling/color. Draft items have no
labels, so their local labels are preserved.

Assignees flow **only into Kanbada**, including on draft items, and only in an
inbound-capable direction. Mapped users replace current assignments; no remote
assignees clears them. Unmapped/inaccessible members are omitted with a visible
warning. Missing API assignee data is an error, not an empty assignment list.
Assignment notifications and card history follow normal workspace behavior.

Enable **Add missing assignees as unregistered members** to match otherwise
unmapped assignees by email and add missing members using their GitHub display
name (or login if no name is set). This is off by default and requires assignee
synchronization in an inbound direction. GitHub often hides email addresses:
missing/invalid emails are skipped with a visible warning, never guessed.
Only assignees encountered on synced items are considered, not the entire directory.
Explicit mappings take precedence, including when a mapped member has left;
fix that mapping rather than silently substituting an email match.

Existing members are reused case-insensitively by email without renaming them.
New members have no user/account ID or login access, even if an account with
that email exists elsewhere in Kanbada. The owner can share their normal
invitation link to let them join; no invitation email is sent automatically.
Display-name collisions get numbered suffixes. No accounts are created, and
unregistered members receive no private assignment notifications. Profile reads
are batched in groups of at most 100. Turning this option off restores
manual-mapping-only behavior. The API flag is `importMissingAssignees`.
Apply the `ImportMissingSynchronizationAssignees` migration before deployment.

**Project status is not issue/PR state.** Sync never closes/reopens issues,
merges/closes pull requests, or propagates deletions. Comments, attachments,
checklists, buckets and swimlanes remain local. Archived, removed, redacted or
inaccessible items are not deleted or recreated. Links are retained as tombstones.

Confirmed inbound-only cards are read-only in Kanbada, including while paused.
Changing direction invalidates workspace caches. All members can use
**Open in GitHub** in card Details; draft items link to their project. Links do not
expose settings or credentials, and GitHub still controls access.

GitHub writes are not transactional across content and project-field mutations.
A selected outbound write remains pending until a reread confirms all mapped
values. Interrupted updates retry from the current Kanbada card before ordinary
conflict detection resumes, preventing partial remote updates from overwriting
the local card. Finish pending outbound work in an outbound-capable direction.
Concurrent human edits during a remote request cannot be protected by an atomic
cross-system compare-and-swap.

## Recovery and scale

The separate worker polls every ten seconds, skips archived/disabled projects,
and holds a PostgreSQL advisory lock per connection. Up to two GitHub connections
can run concurrently; Jira and export workers run independently. Remote project
items use 50-item pages. Nested field values, labels, assignees and project fields
are paginated too. Local outbound cards and unfinished creations use keyset
pages of 50, and EF tracking is cleared between items. Runs do not accumulate
all cards or remote items in memory.

HTTP requests have a 45-second timeout and bounded response size. GraphQL errors
are recognized even with HTTP 200; rate-limit reset/Retry-After values defer
scheduled retries. Error lists are bounded and settings show the first 100
problem links. Fix those and refresh to see remaining problems. Large scans
are allowed to finish rather than restarting at a fixed run deadline.
Settings changes are checked between items; in-flight requests may finish.

Creation has a durable multi-step record:

- Before `createIssue`, reserve the card's creation attempt. A lost response
  never triggers an automatic repeat, because GraphQL `clientMutationId` is
  **not** an idempotency key.
- Once the issue ID is known, persist it before adding it to the project.
  Interrupted project enrollment resumes with that same issue. GitHub returns
  the existing item when the issue is already in the project.
- Complete project fields, reread and verify them, then mark initialization
  complete. Failures retain the link and display a problem.

For an uncertain creation, inspect the configured repository. If the issue
exists, enter its URL and choose **Attach verified issue**, then run again.
Otherwise, only after checking that no issue was created, select the explicit
confirmation and **Allow a new creation attempt**. Incorrect confirmation can
create a duplicate. Resolution is blocked during an active worker run.
Uncertain creates block new unlinked imports/exports until resolved.

## Enterprise Server and deployment

Cloud requests go to `https://api.github.com/graphql`. Enterprise requests go to
`https://HOST/api/graphql`; redirects are disabled. Enterprise versions differ:
use Test to check the required Projects v2 GraphQL capabilities rather than
assuming a minimum release number.

Enterprise authorities require explicit approval. A platform administrator opens
**Server access → Approve this GitHub server** and confirms the hostname/port.
Approval is shared by API and worker through PostgreSQL and takes effect without
a restart. Revocation blocks subsequent requests; deployment-policy hosts remain
allowed. Approve only trusted destinations: credentials and server-side network
access are involved.

Deployment policy can also set `GitHub:AllowedHosts`, a comma-separated list of
exact authorities. Compose passes `GITHUB_ALLOWED_HOSTS` to both services.
Do not include a scheme/path in this environment variable.

Apply the `GitHubProjectsSynchronization` EF migration before production rollout.
Deploy API, worker and portal together. API and worker must share PostgreSQL
and the persistent Data Protection key directory (Compose already mounts it).
The default directory is `.data-protection` under each process's content root;
`GitHub:KeyDirectory` can override it. Back up keys with the database. A missing
or different key ring requires re-entering tokens.

When running the worker from the repository root on the host, set
`GitHub__KeyDirectory` to the absolute `api/.data-protection` directory (and
`Jira__KeyDirectory` to the same directory for Jira). Supply
`ConnectionStrings__Postgres` separately; the worker does not load
`api/appsettings.Local.json`.

The API reference documents owner settings/test/user lookup/run/recovery routes,
member-only card links, and platform-admin host approval routes.

See [GitHub's Projects API guide](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-api-to-manage-projects)
and the matching Enterprise Server version of that guide for token permissions
and available GraphQL operations.
