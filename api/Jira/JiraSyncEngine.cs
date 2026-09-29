using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kanbada.Api;

public sealed class JiraSyncEngine(KanbadaDbContext db, NpgsqlDataSource source, JiraClient jira, ILogger<JiraSyncEngine> logger)
{
    public async Task Run(Guid connectionId, CancellationToken ct)
    {
        await using var lease = await source.OpenConnectionAsync(ct);
        await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@id, 0))", lease);
        acquire.Parameters.AddWithValue("id", "jira:" + connectionId);
        if (await acquire.ExecuteScalarAsync(ct) is not true) return;
        try
        {
            var c = await db.Set<JiraConnectionEntity>().AsNoTracking().Include(c => c.Mappings)
                .SingleOrDefaultAsync(c => c.Id == connectionId && c.Enabled, ct);
            if (c is null || (c.NextRunAt > DateTimeOffset.UtcNow && c.RequestedAt is null)) return;
            if (!await db.Projects.AnyAsync(p => p.WorkspaceId == c.WorkspaceId && p.Id == c.ProjectId && !p.Archived, ct)) return;
            await db.Set<JiraConnectionEntity>().Where(x => x.Id == c.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastStartedAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.RequestedAt, (DateTimeOffset?)null).SetProperty(x => x.LastError, (string?)null), ct);
            var errors = new List<string>();
            var count = 0;
            var started = DateTimeOffset.UtcNow;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            try
            {
                var issues = await jira.Search(c, deadline.Token);
                var ids = issues.Select(i => i.Id).ToHashSet();
                var uncertainCreation = await db.Set<JiraLinkEntity>().AnyAsync(l => l.ConnectionId == c.Id && l.CreationPending, deadline.Token);
                foreach (var issue in issues)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    await Attempt(async () =>
                    {
                        var link = await db.Set<JiraLinkEntity>().SingleOrDefaultAsync(l => l.ConnectionId == c.Id && l.JiraIssueId == issue.Id, deadline.Token);
                        if (link is null)
                        {
                            if (c.Direction == "kanbada-to-jira") return;
                            if (uncertainCreation) throw new JiraSyncException("Resolve the pending Jira creation before importing unlinked issues to avoid duplicate cards.");
                            await Import(c, issue, deadline.Token);
                        }
                        else await Sync(c, link, deadline.Token);
                        count++;
                    }, issue.Key, errors, deadline.Token);
                }
                if (c.Direction != "jira-to-kanbada")
                {
                    db.ChangeTracker.Clear();
                    var cards = await db.Cards.AsNoTracking().Where(card => card.WorkspaceId == c.WorkspaceId && card.ProjectId == c.ProjectId
                        && !db.Set<JiraLinkEntity>().Any(l => l.ConnectionId == c.Id && l.CardId == card.Id)).Select(card => card.Id).ToListAsync(deadline.Token);
                    foreach (var card in cards)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        await Attempt(async () => { await Export(c, card, deadline.Token); count++; }, card, errors, deadline.Token);
                    }
                }
                db.ChangeTracker.Clear();
                var links = await db.Set<JiraLinkEntity>().Where(l => l.ConnectionId == c.Id).ToListAsync(deadline.Token);
                foreach (var link in links)
                {
                    if (link.CreationPending)
                        link.LastError = "Jira creation may have succeeded. Check Jira and attach its numeric issue ID before retrying; automatic creation is blocked to avoid duplicates.";
                    else if (link.JiraIssueId is not null && !ids.Contains(link.JiraIssueId) && (link.LastSyncedAt is null || link.LastSyncedAt < started))
                        link.LastError = "Issue is outside the current JQL, not yet indexed, deleted, or inaccessible. No updates or deletions are propagated.";
                }
                await db.SaveChangesAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                errors.Add(SafeError(error));
                logger.LogError("Jira run {ConnectionId} failed ({ErrorType}).", c.Id, error.GetType().Name);
            }
            db.ChangeTracker.Clear();
            // Respect settings edits made while a run was in flight.
            var latest = await db.Set<JiraConnectionEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.Id, ct);
            if (latest is not null)
            {
                var problemCount = await db.Set<JiraLinkEntity>().CountAsync(l => l.ConnectionId == c.Id && l.LastError != null, ct);
                var message = errors.Count > 0 ? string.Join("\n", errors.Take(10))
                    : problemCount > 0 ? $"{problemCount} linked items need attention. Open synchronization settings for details." : null;
                await db.Set<JiraConnectionEntity>().Where(x => x.Id == c.Id && x.Version == latest.Version)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastFinishedAt, DateTimeOffset.UtcNow)
                        .SetProperty(x => x.NextRunAt, JiraSchedule.Next(latest.Cron, latest.TimeZone, DateTimeOffset.UtcNow))
                        .SetProperty(x => x.LastError, message).SetProperty(x => x.LastSyncedCount, count), ct);
            }
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@id, 0))", lease);
            release.Parameters.AddWithValue("id", "jira:" + connectionId);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task Attempt(Func<Task> action, string item, List<string> errors, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        try { await action(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var message = SafeError(error);
            errors.Add(item + ": " + message);
            logger.LogWarning("Jira item {Item} failed ({ErrorType}).", item, error.GetType().Name);
            var linkId = db.ChangeTracker.Entries<JiraLinkEntity>().Select(e => (Guid?)e.Entity.Id).FirstOrDefault();
            db.ChangeTracker.Clear();
            if (linkId.HasValue)
                await db.Set<JiraLinkEntity>().Where(l => l.Id == linkId.Value).ExecuteUpdateAsync(s => s.SetProperty(l => l.LastError, message), ct);
        }
    }

    private static string SafeError(Exception error) => error switch
    {
        JiraSyncException => error.Message,
        ApiError => error.Message,
        DbUpdateConcurrencyException => "Kanbada changed during synchronization. The item will be retried on the next run.",
        PostgresException { SqlState: "23505", ConstraintName: "labels_workspace_normalized_name" } =>
            "Workspace labels changed during synchronization. The item will be retried on the next run.",
        DbUpdateException { InnerException: PostgresException { SqlState: "23505", ConstraintName: "labels_workspace_normalized_name" } } =>
            "Workspace labels changed during synchronization. The item will be retried on the next run.",
        OperationCanceledException => "Jira request or run timed out. Inspect pending creations before retrying.",
        HttpRequestException => "Cannot reach Jira. Check its hostname, TLS certificate and network connectivity.",
        _ => $"Synchronization failed ({error.GetType().Name}). Inspect worker logs and configuration."
    };

    private async Task<JiraSnapshot> Snapshot(CardEntity card, CancellationToken ct)
    {
        var labels = await CardSynchronization.Labels(db, card, ct);
        return JiraSnapshot.FromCard(card, labels);
    }

    private async Task Import(JiraConnectionEntity c, JiraIssue issue, CancellationToken ct)
    {
        var snapshot = JiraSnapshot.FromIssue(c, issue);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == c.WorkspaceId, ct);
        var card = new CardEntity
        {
            WorkspaceId = c.WorkspaceId,
            Id = "KB-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ProjectId = c.ProjectId,
            Position = (await db.Cards.Where(x => x.WorkspaceId == c.WorkspaceId).MaxAsync(x => (int?)x.Position, ct) ?? -1) + 1
        };
        db.Add(card);
        await Apply(card, snapshot, ct);
        var assignee = await ApplyAssignee(c, card, issue, ct);
        db.Add(new JiraLinkEntity
        {
            Id = Guid.NewGuid(),
            ConnectionId = c.Id,
            CardId = card.Id,
            JiraIssueId = issue.Id,
            JiraKey = issue.Key,
            Origin = "jira",
            KanbadaHash = snapshot.Hash(),
            JiraHash = snapshot.Hash(),
            LastError = assignee.Warning,
            LastSyncedAt = DateTimeOffset.UtcNow
        });
        await SaveWorkspace(workspace, card, "Created from Jira " + issue.Key, ct);
        await tx.CommitAsync(ct);
    }

    private async Task Export(JiraConnectionEntity c, string cardId, CancellationToken ct)
    {
        var card = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == c.WorkspaceId && x.ProjectId == c.ProjectId && x.Id == cardId, ct);
        if (card is null) return;
        var snapshot = await Snapshot(card, ct);
        // Validate mappings before reserving a creation attempt.
        JiraClient.ValidateWrite(c, snapshot);
        var link = new JiraLinkEntity { Id = Guid.NewGuid(), ConnectionId = c.Id, CardId = cardId, Origin = "kanbada", CreationPending = true };
        db.Add(link);
        await db.SaveChangesAsync(ct);
        (string Id, string Key) created;
        try { created = await jira.Create(c, snapshot, ct); }
        catch (JiraHttpException error) when (error.Status is System.Net.HttpStatusCode.BadRequest
            or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.UnprocessableEntity
            or System.Net.HttpStatusCode.TooManyRequests)
        {
            db.Remove(link);
            await db.SaveChangesAsync(ct);
            throw;
        }
        link.JiraIssueId = created.Id;
        link.JiraKey = created.Key;
        link.CreationPending = false;
        await ExternalCardPolicy.InvalidateWorkspace(db, c.WorkspaceId, ct);
        await db.SaveChangesAsync(ct);
        var issue = await jira.Get(c, created.Id, ct);
        await jira.Transition(c, issue, snapshot.Status, ct);
        var actual = JiraSnapshot.FromIssue(c, await jira.Get(c, created.Id, ct));
        if (snapshot.Hash() != actual.Hash()) throw new JiraSyncException("Jira did not retain all requested field values. Check workflow rules before retrying.");
        link.KanbadaHash = snapshot.Hash();
        link.JiraHash = actual.Hash();
        link.LastSyncedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task Sync(JiraConnectionEntity c, JiraLinkEntity link, CancellationToken ct)
    {
        var issue = await jira.Get(c, link.JiraIssueId!, ct);
        var remote = JiraSnapshot.FromIssue(c, issue);
        var card = await db.Cards.SingleOrDefaultAsync(x => x.WorkspaceId == c.WorkspaceId && x.Id == link.CardId, ct);
        if (card is null || card.ProjectId != c.ProjectId)
            throw new JiraSyncException("The linked Kanbada card was deleted or moved. No deletion or recreation is propagated.");
        var local = await Snapshot(card, ct);
        var winner = JiraSnapshot.Winner(c.Direction, link.Origin, local.Hash(), remote.Hash(), link.KanbadaHash, link.JiraHash);
        if (winner == "jira")
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Reload the card and version within the same transaction; reject a concurrent local edit.
            var workspace = await db.Workspaces.SingleAsync(w => w.Id == c.WorkspaceId, ct);
            await db.Entry(card).ReloadAsync(ct);
            if ((await Snapshot(card, ct)).Hash() != local.Hash() || card.ProjectId != c.ProjectId)
                throw new DbUpdateConcurrencyException();
            await Apply(card, remote, ct);
            var assignee = await ApplyAssignee(c, card, issue, ct);
            link.KanbadaHash = remote.Hash();
            link.JiraHash = remote.Hash();
            link.JiraKey = issue.Key;
            link.LastError = assignee.Warning;
            link.LastSyncedAt = DateTimeOffset.UtcNow;
            await SaveWorkspace(workspace, card, "Synchronized from Jira " + issue.Key, ct);
            await tx.CommitAsync(ct);
            return;
        }
        if (winner == "kanbada")
        {
            await jira.Update(c, issue, local, ct);
            issue = await jira.Get(c, issue.Id, ct);
            remote = JiraSnapshot.FromIssue(c, issue);
            if (remote.Hash() != local.Hash()) throw new JiraSyncException("Jira did not retain all requested field values. Check workflow rules before retrying.");
        }
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var workspace = await db.Workspaces.SingleAsync(w => w.Id == c.WorkspaceId, ct);
            await db.Entry(card).ReloadAsync(ct);
            if (card.ProjectId != c.ProjectId) throw new DbUpdateConcurrencyException();
            var assignee = await ApplyAssignee(c, card, issue, ct);
            link.KanbadaHash = local.Hash();
            link.JiraHash = remote.Hash();
            link.JiraKey = issue.Key;
            link.LastError = assignee.Warning;
            link.LastSyncedAt = DateTimeOffset.UtcNow;
            if (assignee.Changed)
                await SaveWorkspace(workspace, card, "Assignee synchronized from Jira " + issue.Key, ct);
            else await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }

    private async Task<(bool Changed, string? Warning)> ApplyAssignee(JiraConnectionEntity c, CardEntity card, JiraIssue issue, CancellationToken ct)
    {
        if (!c.SyncAssignees || c.Direction == "kanbada-to-jira") return (false, null);
        if (!issue.Fields.ContainsKey("assignee"))
            throw new JiraSyncException("Jira did not return the assignee field. No assignee changes were saved.");
        string? email = null;
        string? warning = null;
        if (issue.Fields["assignee"] is not null)
        {
            var identity = JiraClient.AssigneeId(issue.Fields["assignee"]!.AsObject(), c.Edition);
            var mapping = c.Mappings.SingleOrDefault(m => m.Kind == "assignee" && m.JiraValue == identity);
            if (mapping is not null && Guid.TryParse(mapping.KanbadaValue, out var user))
                email = await db.Members.Where(m => m.WorkspaceId == c.WorkspaceId && m.UserId == user)
                    .Select(m => m.Email).SingleOrDefaultAsync(ct);
            if (email is null)
            {
                warning = $"Assignee mapping warning: Jira user {identity} has no mapped workspace member. The card was left unassigned.";
                logger.LogWarning("Jira item {Item}: {Warning}", issue.Key, warning);
            }
        }
        var existing = await db.CardAssignees.Where(a => a.WorkspaceId == card.WorkspaceId && a.CardId == card.Id).ToListAsync(ct);
        var removed = existing.Where(a => a.MemberEmail != email).ToList();
        db.RemoveRange(removed);
        var added = email is not null && !existing.Any(a => a.MemberEmail == email);
        if (added)
            db.Add(new CardAssigneeEntity { WorkspaceId = card.WorkspaceId, CardId = card.Id, MemberEmail = email!, Position = 0 });
        return (removed.Count > 0 || added, warning);
    }

    private async Task Apply(CardEntity card, JiraSnapshot snapshot, CancellationToken ct)
    {
        if (!await db.Statuses.AnyAsync(s => s.WorkspaceId == card.WorkspaceId && s.Id == snapshot.Status, ct))
            throw new JiraSyncException("The mapped Kanbada status no longer exists.");
        card.Title = snapshot.Title;
        card.Description = snapshot.DescriptionText;
        card.StatusId = snapshot.Status;
        card.Priority = snapshot.Priority;
        card.Due = snapshot.Due;
        await CardSynchronization.ApplyLabels(db, card, snapshot.Labels, "jira", ct);
    }

    private Task SaveWorkspace(WorkspaceEntity workspace, CardEntity card, string change, CancellationToken ct) =>
        CardSynchronization.SaveWorkspace(db, workspace, card, "Jira synchronization", change, ct);
}
