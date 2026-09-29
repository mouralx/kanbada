using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kanbada.Api;

public sealed class GitHubSyncEngine(KanbadaDbContext db, NpgsqlDataSource source, GitHubClient github, ILogger<GitHubSyncEngine> logger)
{
    private sealed class SettingsChangedException : Exception;
    public const string PendingMessage = "GitHub issue creation may have succeeded. Verify GitHub and attach the issue URL, or explicitly confirm that no issue was created before retrying.";

    public async Task Run(Guid connectionId, CancellationToken ct)
    {
        await using var lease = await source.OpenConnectionAsync(ct);
        await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@id, 0))", lease);
        acquire.Parameters.AddWithValue("id", "github:" + connectionId);
        if (await acquire.ExecuteScalarAsync(ct) is not true) return;
        try
        {
            var c = await db.Set<GitHubConnectionEntity>().AsNoTracking().Include(x => x.Mappings).SingleOrDefaultAsync(x => x.Id == connectionId && x.Enabled, ct);
            if (c is null || (c.NextRunAt > DateTimeOffset.UtcNow && c.RequestedAt is null)) return;
            if (!await db.Projects.AnyAsync(p => p.WorkspaceId == c.WorkspaceId && p.Id == c.ProjectId && !p.Archived, ct)) return;
            await db.Set<GitHubConnectionEntity>().Where(x => x.Id == c.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastStartedAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.NextRunAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.RequestedAt, (DateTimeOffset?)null).SetProperty(x => x.LastError, (string?)null), ct);
            var runId = Guid.NewGuid();
            var errors = new List<string>();
            var count = 0;
            DateTimeOffset? retryAt = null;
            try
            {
                var metadata = await github.Metadata(c, ct);
                if (metadata.ProjectId != c.RemoteProjectId || metadata.RepositoryId != c.RepositoryId)
                    throw new GitHubSyncException("The configured GitHub project or repository identity changed. Review connection settings.");
                if (!metadata.Fields.Any(f => f.Id == c.StatusFieldId && f.DataType == "SINGLE_SELECT")
                    || (c.PriorityFieldId.Length > 0 && !metadata.Fields.Any(f => f.Id == c.PriorityFieldId && f.DataType == "SINGLE_SELECT"))
                    || (c.DueFieldId.Length > 0 && !metadata.Fields.Any(f => f.Id == c.DueFieldId && f.DataType == "DATE")))
                    throw new GitHubSyncException("A configured GitHub field was removed or changed type. Review field mappings.");
                string? after = null;
                do
                {
                    db.ChangeTracker.Clear();
                    var pending = await db.Set<GitHubLinkEntity>().AsNoTracking().Where(l => l.ConnectionId == c.Id && l.ContentId != null
                        && !l.CreationPending && !l.Initialized && (after == null || l.CardId.CompareTo(after) > 0))
                        .OrderBy(l => l.CardId).Select(l => new { l.Id, l.CardId }).Take(50).ToListAsync(ct);
                    if (pending.Count == 0) break;
                    foreach (var link in pending)
                    {
                        await Current(c, ct);
                        await Attempt(async () => await CompleteOutbound(c, await db.Set<GitHubLinkEntity>().SingleAsync(l => l.Id == link.Id, ct), ct),
                            link.CardId, errors, ct);
                    }
                    after = pending[^1].CardId;
                } while (true);
                var uncertain = await db.Set<GitHubLinkEntity>().AnyAsync(l => l.ConnectionId == c.Id && l.CreationPending, ct);
                await foreach (var item in github.Items(c, ct))
                {
                    await Current(c, ct);
                    await Attempt(async () =>
                    {
                        var link = await db.Set<GitHubLinkEntity>().SingleOrDefaultAsync(l => l.ConnectionId == c.Id && l.ItemId == item.Id, ct);
                        if (link is null)
                        {
                            if (item.Archived || c.Direction == "kanbada-to-github") return;
                            if (uncertain) throw new GitHubSyncException("Resolve pending issue creations before importing unlinked GitHub items to avoid duplicate cards.");
                            await Import(c, item, runId, ct);
                        }
                        else
                        {
                            await db.Set<GitHubLinkEntity>().Where(l => l.Id == link.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.SeenRunId, runId), ct);
                            link.SeenRunId = runId;
                            if (!link.Initialized) throw new GitHubSyncException("The outbound issue is still being initialized. Review its pending field mappings.");
                            await Sync(c, link, ct);
                        }
                        count++;
                    }, item.Content?.Key ?? item.Id, errors, ct);
                }
                await db.Set<GitHubLinkEntity>().Where(l => l.ConnectionId == c.Id && l.ItemId != null && l.Initialized && l.SeenRunId != runId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastError, "Item is outside the GitHub project, deleted or inaccessible. No deletion or recreation is propagated."), ct);
                if (c.Direction != "github-to-kanbada")
                {
                    if (uncertain) throw new GitHubSyncException("Resolve the pending GitHub issue creation before exporting more cards.");
                    after = null;
                    do
                    {
                        db.ChangeTracker.Clear();
                        var cards = await db.Cards.AsNoTracking().Where(card => card.WorkspaceId == c.WorkspaceId && card.ProjectId == c.ProjectId
                            && (after == null || card.Id.CompareTo(after) > 0) && !db.Set<GitHubLinkEntity>().Any(l => l.ConnectionId == c.Id && l.CardId == card.Id))
                            .OrderBy(card => card.Id).Select(card => card.Id).Take(50).ToListAsync(ct);
                        if (cards.Count == 0) break;
                        foreach (var card in cards)
                        {
                            await Current(c, ct);
                            await Attempt(async () => { await Export(c, card, ct); count++; }, card, errors, ct);
                        }
                        after = cards[^1];
                    } while (true);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                if (errors.Count < 10) errors.Add(SafeError(error));
                retryAt = (error as GitHubSyncException)?.RetryAt;
                logger.LogError("GitHub run {ConnectionId} failed ({ErrorType}).", c.Id, error.GetType().Name);
            }
            db.ChangeTracker.Clear();
            var latest = await db.Set<GitHubConnectionEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.Id, ct);
            if (latest is not null)
            {
                var problems = await db.Set<GitHubLinkEntity>().CountAsync(l => l.ConnectionId == c.Id && (l.LastError != null || l.CreationPending), ct);
                var message = errors.Count > 0 ? string.Join("\n", errors)
                    : problems > 0 ? $"{problems} linked items need attention. Open synchronization settings for details." : null;
                var next = JiraSchedule.Next(latest.Cron, latest.TimeZone, DateTimeOffset.UtcNow);
                if (retryAt > next) next = retryAt.Value;
                await db.Set<GitHubConnectionEntity>().Where(x => x.Id == c.Id && x.Version == latest.Version)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastFinishedAt, DateTimeOffset.UtcNow).SetProperty(x => x.NextRunAt, next)
                        .SetProperty(x => x.LastError, message).SetProperty(x => x.LastSyncedCount, count), ct);
            }
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@id, 0))", lease);
            release.Parameters.AddWithValue("id", "github:" + connectionId);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task Current(GitHubConnectionEntity c, CancellationToken ct)
    {
        if (!await db.Set<GitHubConnectionEntity>().AnyAsync(x => x.Id == c.Id && x.Version == c.Version && x.Enabled, ct)
            || !await db.Projects.AnyAsync(p => p.WorkspaceId == c.WorkspaceId && p.Id == c.ProjectId && !p.Archived, ct))
            throw new SettingsChangedException();
    }

    private async Task Attempt(Func<Task> action, string item, List<string> errors, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        try { await action(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var message = SafeError(error);
            if (errors.Count < 10) errors.Add(item + ": " + message);
            logger.LogWarning("GitHub item {Item} failed ({ErrorType}).", item, error.GetType().Name);
            var trackedLink = db.ChangeTracker.Entries<GitHubLinkEntity>().Select(e => e.Entity).FirstOrDefault();
            var linkId = trackedLink?.Id;
            db.ChangeTracker.Clear();
            if (linkId.HasValue)
                await db.Set<GitHubLinkEntity>().Where(l => l.Id == linkId.Value).ExecuteUpdateAsync(s => s.SetProperty(l => l.LastError, message), ct);
            if (trackedLink?.CreationPending == true || error is SettingsChangedException or HttpRequestException or OperationCanceledException
                || error is GitHubSyncException { RetryAt: not null }) throw;
        }
    }

    public static string SafeError(Exception error) => error switch
    {
        GitHubSyncException or ApiError => error.Message,
        SettingsChangedException => "Synchronization settings changed or the project was archived. The next run uses the latest settings.",
        DbUpdateConcurrencyException => "Kanbada changed during synchronization. The item will be retried on the next run.",
        OperationCanceledException => "The GitHub request timed out. Inspect pending issue creations before retrying.",
        HttpRequestException => "Cannot reach GitHub. Check its hostname, TLS certificate and network connectivity.",
        _ => $"GitHub synchronization failed ({error.GetType().Name}). Inspect worker logs and configuration."
    };

    private async Task<GitHubSnapshot> Snapshot(GitHubConnectionEntity c, CardEntity card, bool draft, CancellationToken ct) =>
        GitHubSnapshot.FromCard(c, card, await CardSynchronization.Labels(db, card, ct), draft);

    private async Task Import(GitHubConnectionEntity c, GitHubItem item, Guid runId, CancellationToken ct)
    {
        var snapshot = GitHubSnapshot.FromItem(c, item);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Current(c, ct);
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == c.WorkspaceId, ct);
        var card = new CardEntity
        {
            WorkspaceId = c.WorkspaceId, Id = "KB-" + Guid.NewGuid().ToString("N").ToUpperInvariant(), ProjectId = c.ProjectId, Priority = "Medium",
            Position = (await db.Cards.Where(x => x.WorkspaceId == c.WorkspaceId).MaxAsync(x => (int?)x.Position, ct) ?? -1) + 1
        };
        db.Add(card);
        await Apply(c, card, snapshot, ct);
        var assignees = await ApplyAssignees(c, card, item.Content!, ct);
        var link = new GitHubLinkEntity { Id = Guid.NewGuid(), ConnectionId = c.Id, CardId = card.Id, ItemId = item.Id, Origin = "github", Initialized = true, SeenRunId = runId };
        SetBaseline(link, item.Content!, snapshot.Hash(), snapshot.Hash(), assignees.Warning);
        db.Add(link);
        await CardSynchronization.SaveWorkspace(db, workspace, card, "GitHub synchronization", "Created from GitHub " + item.Content!.Key, ct);
        await tx.CommitAsync(ct);
    }

    private async Task Export(GitHubConnectionEntity c, string cardId, CancellationToken ct)
    {
        var card = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == c.WorkspaceId && x.ProjectId == c.ProjectId && x.Id == cardId, ct);
        if (card is null) throw new GitHubSyncException("The Kanbada card was deleted or moved before synchronization.");
        var snapshot = await Snapshot(c, card, false, ct);
        GitHubSnapshot.ValidateWrite(c, snapshot);
        var labels = snapshot.Labels is null ? null : await github.LabelIds(c, c.RepositoryId, snapshot.Labels, ct);
        await Current(c, ct);
        var link = new GitHubLinkEntity { Id = Guid.NewGuid(), ConnectionId = c.Id, CardId = cardId, Origin = "kanbada", CreationPending = true, LastError = PendingMessage };
        db.Add(link);
        await db.SaveChangesAsync(ct);
        // A GraphQL clientMutationId is not an idempotency key. Never repeat an uncertain create.
        link.ContentId = await github.CreateIssue(c, snapshot, labels, ct);
        link.ContentType = "Issue";
        link.CreationPending = false;
        link.LastError = "Issue created. Project attachment and initial field synchronization are pending.";
        await db.SaveChangesAsync(ct);
        await CompleteOutbound(c, link, ct);
    }

    private async Task CompleteOutbound(GitHubConnectionEntity c, GitHubLinkEntity link, CancellationToken ct)
    {
        if (c.Direction == "github-to-kanbada") throw new GitHubSyncException("An unfinished outbound issue requires an outbound direction to finish initialization.");
        await Current(c, ct);
        var card = await LinkedCard(c, link, ct);
        var snapshot = await Snapshot(c, card, false, ct);
        GitHubSnapshot.ValidateWrite(c, snapshot);
        if (link.ItemId is null)
        {
            // Adding an existing issue to the same Projects v2 project returns the existing item.
            link.ItemId = await github.AddItem(c, link.ContentId!, ct);
            await db.SaveChangesAsync(ct);
        }
        var item = await github.Item(c, link.ItemId, ct);
        if (item.Archived || item.Content?.Id != link.ContentId) throw new GitHubSyncException("The pending GitHub issue was archived or changed identity.");
        await Current(c, ct);
        await github.Update(c, item, snapshot, ct);
        item = await github.Item(c, link.ItemId, ct);
        var actual = GitHubSnapshot.FromItem(c, item);
        if (snapshot.Hash() != actual.Hash()) throw new GitHubSyncException("GitHub did not retain the requested field values. Review repository/project rules and mappings.");
        link.Initialized = true;
        SetBaseline(link, item.Content!, snapshot.Hash(), actual.Hash(), null);
        await db.SaveChangesAsync(ct);
    }

    private async Task<CardEntity> LinkedCard(GitHubConnectionEntity c, GitHubLinkEntity link, CancellationToken ct)
    {
        var card = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == c.WorkspaceId && x.Id == link.CardId, ct);
        if (card is null || card.ProjectId != c.ProjectId)
            throw new GitHubSyncException("The linked Kanbada card was deleted or moved. No deletion or recreation is propagated.");
        return card;
    }

    private async Task Sync(GitHubConnectionEntity c, GitHubLinkEntity link, CancellationToken ct)
    {
        var item = await github.Item(c, link.ItemId!, ct);
        var remote = GitHubSnapshot.FromItem(c, item);
        var card = await LinkedCard(c, link, ct);
        var local = await Snapshot(c, card, item.Content!.Type == "DraftIssue", ct);
        var winner = CardSynchronization.Winner("github", c.Direction, link.Origin, local.Hash(), remote.Hash(), link.KanbadaHash, link.GitHubHash);
        if (link.WritePending)
        {
            if (c.Direction == "github-to-kanbada") throw new GitHubSyncException("An unconfirmed GitHub update requires an outbound direction to finish. Partial remote values were not imported.");
            winner = "kanbada";
        }
        if (winner == "kanbada")
        {
            GitHubSnapshot.ValidateWrite(c, local);
            await Current(c, ct);
            link.WritePending = true;
            link.LastError = "Outbound GitHub update is pending confirmation.";
            await db.SaveChangesAsync(ct);
            await github.Update(c, item, local, ct);
            item = await github.Item(c, item.Id, ct);
            remote = GitHubSnapshot.FromItem(c, item);
            if (remote.Hash() != local.Hash()) throw new GitHubSyncException("GitHub did not retain the requested field values. Review repository/project rules and mappings.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Current(c, ct);
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == c.WorkspaceId, ct);
        await db.Entry(card).ReloadAsync(ct);
        if (card.ProjectId != c.ProjectId || (winner == "github" && (await Snapshot(c, card, item.Content!.Type == "DraftIssue", ct)).Hash() != local.Hash()))
            throw new DbUpdateConcurrencyException();
        if (winner == "github") await Apply(c, card, remote, ct);
        var assignees = await ApplyAssignees(c, card, item.Content!, ct);
        link.WritePending = false;
        SetBaseline(link, item.Content!, winner == "github" ? remote.Hash() : local.Hash(), remote.Hash(), assignees.Warning);
        if (winner == "github" || assignees.Changed)
            await CardSynchronization.SaveWorkspace(db, workspace, card, "GitHub synchronization", "Synchronized from GitHub " + item.Content!.Key, ct);
        else await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static void SetBaseline(GitHubLinkEntity link, GitHubContent content, string local, string remote, string? warning)
    {
        link.ContentId = content.Id; link.ContentType = content.Type; link.Url = content.Url; link.DisplayKey = content.Key;
        link.KanbadaHash = local; link.GitHubHash = remote; link.LastError = warning; link.LastSyncedAt = DateTimeOffset.UtcNow;
    }

    private async Task Apply(GitHubConnectionEntity c, CardEntity card, GitHubSnapshot snapshot, CancellationToken ct)
    {
        if (!await db.Statuses.AnyAsync(s => s.WorkspaceId == card.WorkspaceId && s.Id == snapshot.Status, ct))
            throw new GitHubSyncException("The mapped Kanbada status no longer exists.");
        card.Title = snapshot.Title; card.Description = snapshot.Body; card.StatusId = snapshot.Status;
        if (snapshot.Priority is not null) card.Priority = snapshot.Priority;
        if (c.DueFieldId.Length > 0) card.Due = snapshot.Due;
        if (snapshot.Labels is not null) await CardSynchronization.ApplyLabels(db, card, snapshot.Labels, "github", ct);
    }

    private async Task<(bool Changed, string? Warning)> ApplyAssignees(GitHubConnectionEntity c, CardEntity card, GitHubContent content, CancellationToken ct)
    {
        if (!c.SyncAssignees || c.Direction == "kanbada-to-github") return (false, null);
        var emails = new HashSet<string>();
        var unmapped = 0;
        foreach (var id in content.Assignees)
        {
            var mapping = c.Mappings.SingleOrDefault(m => m.Kind == "assignee" && m.GitHubValue == id);
            var email = mapping is not null && Guid.TryParse(mapping.KanbadaValue, out var user)
                ? await db.Members.Where(m => m.WorkspaceId == c.WorkspaceId && m.UserId == user).Select(m => m.Email).SingleOrDefaultAsync(ct) : null;
            if (email is null) unmapped++; else emails.Add(email);
        }
        var existing = await db.CardAssignees.Where(a => a.WorkspaceId == card.WorkspaceId && a.CardId == card.Id).ToListAsync(ct);
        var removed = existing.Where(a => !emails.Contains(a.MemberEmail)).ToList();
        var added = emails.Where(e => !existing.Any(a => a.MemberEmail == e)).ToList();
        db.RemoveRange(removed);
        foreach (var email in added)
            db.Add(new CardAssigneeEntity { WorkspaceId = card.WorkspaceId, CardId = card.Id, MemberEmail = email, Position = existing.Count + added.IndexOf(email) });
        return (removed.Count + added.Count > 0, unmapped == 0 ? null : $"{unmapped} GitHub assignees have no mapped workspace member. Only mapped members were assigned.");
    }
}
