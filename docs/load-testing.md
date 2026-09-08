# Large local dataset

`tools/Kanbada.LoadData` is an explicit destructive EF Core maintenance command. It removes all workspace data and all accounts except the specified existing user, then generates a repeatable workload. The retained user's credentials, provider identities and sessions are preserved. It creates no additional users and sends no invitations or messages.

Stop the API and take a verified PostgreSQL backup before running this from the repository root:

```sh
dotnet run --project tools/Kanbada.LoadData -- --reset-keep-user nuno.moura@outlook.com
```

It uses the same local/environment connection configuration as EF migrations. The reset and seed run in one transaction; a failure rolls back both. Writes use EF Core in batches of 250 cards. Migration history is preserved. The user must already exist and match exactly one account.

Dataset:

- 5 workspaces with 500, 2500, 5000, 5000 and 7000 cards: 20000 cards total.
- 20 projects per workspace, including protected My activities and 2 archived projects.
- 6 statuses, 10 buckets, 20 labels and 80 project swimlanes per workspace.
- 40000 card-label links, 16000 assignments to the retained user, 60000 comments, 100000 checklist items, 80000 history entries and 80000 history changes.
- 1000 downloadable text attachments of about 65 KB each, 200 members-only share links, 500 notifications and 150 activity entries.
- Mixed completed, blocked, overdue, future-dated, undated and unassigned cards.

Reload the portal after the reset. Old workspace/card URLs no longer point to existing records. The personal workspace alias remains `studio`.

This seeds a large dataset; it does not generate concurrent traffic. Use it to measure workspace loading, board/list rendering, filtering, dashboards, PDF export, card editing and attachment downloads. Larger workspaces intentionally stress the current full-snapshot reads and client-side rendering. Record browser timings, API latency and memory before drawing conclusions about concurrent-user capacity.

A read-only application-service check is available with `dotnet run --project tools/Kanbada.LoadData -- --verify-user nuno.moura@outlook.com`. It validates access to the workspaces, filtered metrics and complete snapshots for all five workspaces without creating extra test users.

## Mapping performance fix

Workspace response mapping now groups assignments, labels, comments, checklists, history entries and history changes once, then looks them up by card/history key. It no longer scans complete association collections for each card. Automatic EF change detection is temporarily disabled only during read mapping and explicit tracked-value synchronization, with the previous setting restored in `finally`; normal save-time change detection and optimistic concurrency remain enabled. See [Microsoft's change-detection guidance](https://learn.microsoft.com/en-us/ef/core/change-tracking/change-detection).

A local run against the seeded dataset reduced the 500-card read/validation from 88.30 seconds to 0.39 seconds. The 7000-card read/validation/serialization took 1.15 seconds. These are application-service timings, not browser/network latency or concurrent-user benchmarks. The largest snapshot remains about 12 MB; project-scoped/paginated reads and loading history only when a card opens are the next architectural improvements for reducing that initial download.

`WorkspaceMappingTests` uses an isolated 1000-card fixture to verify bounded change-detection scans, correct history grouping/order, and persisted edits after reading. It does not add users to the seeded application database.
