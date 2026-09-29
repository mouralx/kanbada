using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public sealed class JiraSettings(KanbadaDbContext db, WorkspaceResolver resolver, JiraSecrets secrets, JiraDestinationPolicy policy)
{
    public async Task<JiraConnectionEntity> ForTest(Guid workspace, string project, JiraConnectionInput input, CancellationToken ct)
    {
        if (input.Email is null) throw new ApiError(400, "Provide an email value (empty for a Data Center PAT).");
        if (input.Edition is not ("cloud" or "data-center")) throw new ApiError(400, "Select Jira Cloud or Data Center.");
        var url = policy.Validate(input.BaseUrl, input.Edition).AbsoluteUri;
        if (input.Edition == "cloud" && !System.Net.Mail.MailAddress.TryCreate(input.Email, out _))
            throw new ApiError(400, "Jira Cloud requires an account email.");
        if (string.IsNullOrWhiteSpace(input.JiraProjectKey) || input.JiraProjectKey.Length > 80
            || string.IsNullOrWhiteSpace(input.Jql) || input.Jql.Length > 10000)
            throw new ApiError(400, "Provide the Jira project key and JQL before testing.");
        var stored = await Find(workspace, project, ct);
        var token = input.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            if (stored is null || stored.BaseUrl != url || stored.Edition != input.Edition || stored.Email != input.Email.Trim())
                throw new ApiError(400, "Enter a token for this Jira account.");
            token = secrets.Unprotect(stored.ProtectedToken);
        }
        if (token.Length > 4096 || token.Any(char.IsControl)) throw new ApiError(400, "Invalid Jira token.");
        return new JiraConnectionEntity
        {
            BaseUrl = url,
            Edition = input.Edition,
            Email = input.Email.Trim(),
            ProtectedToken = secrets.Protect(token),
            JiraProjectKey = input.JiraProjectKey,
            Jql = input.Jql,
            Mappings = stored is not null && stored.BaseUrl == url && stored.Edition == input.Edition ? stored.Mappings : []
        };
    }

    public async Task<Guid> Authorize(string workspaceId, string projectId, Guid user, CancellationToken ct)
    {
        var id = await resolver.WorkspaceId(workspaceId, user);
        if (!await db.Workspaces.AnyAsync(w => w.Id == id && w.OwnerId == user, ct))
            throw new ApiError(403, "Only the workspace owner can manage Jira synchronization.");
        if (!await db.Projects.AnyAsync(p => p.WorkspaceId == id && p.Id == projectId, ct))
            throw new ApiError(404, "Project not found.");
        return id;
    }

    public Task<JiraConnectionEntity?> Find(Guid workspace, string project, CancellationToken ct) =>
        db.Set<JiraConnectionEntity>().Include(c => c.Mappings)
            .SingleOrDefaultAsync(c => c.WorkspaceId == workspace && c.ProjectId == project, ct);

    public async Task<object?> Read(Guid workspace, string project, CancellationToken ct)
    {
        var c = await Find(workspace, project, ct);
        if (c is null) return null;
        var problems = await db.Set<JiraLinkEntity>().AsNoTracking()
            .Where(l => l.ConnectionId == c.Id && (l.LastError != null || l.CreationPending))
            .OrderBy(l => l.CardId).Take(100)
            .Select(l => new { l.Id, l.CardId, l.JiraKey, l.JiraIssueId, l.CreationPending, l.LastError }).ToListAsync(ct);
        return new
        {
            c.Id,
            c.Version,
            c.BaseUrl,
            c.Edition,
            c.Email,
            HasToken = c.ProtectedToken.Length > 0,
            c.Jql,
            c.JiraProjectKey,
            c.IssueTypeId,
            c.Direction,
            c.Cron,
            c.TimeZone,
            c.Enabled,
            c.SyncAssignees,
            c.ImportMissingAssignees,
            c.NextRunAt,
            c.RequestedAt,
            c.LastStartedAt,
            c.LastFinishedAt,
            c.LastError,
            c.LastSyncedCount,
            Mappings = c.Mappings.Select(m => new JiraMappingInput(m.Kind, m.KanbadaValue, m.JiraValue, m.IsDefault)),
            Problems = problems
        };
    }

    public async Task Save(Guid workspace, string project, JiraConnectionInput input, CancellationToken ct)
    {
        JiraConfiguration.Validate(input, policy);
        var statuses = await db.Statuses.Where(s => s.WorkspaceId == workspace).Select(s => s.Id).ToListAsync(ct);
        if (input.Mappings.Any(m => m.Kind == "status" && !statuses.Contains(m.KanbadaValue)))
            throw new ApiError(400, "A mapped Kanbada status no longer exists.");
        var members = await db.Members.Where(m => m.WorkspaceId == workspace && m.UserId != null)
            .Select(m => m.UserId!.Value).ToListAsync(ct);
        if (input.Mappings.Any(m => m.Kind == "assignee" && !members.Contains(Guid.Parse(m.KanbadaValue))))
            throw new ApiError(400, "Assignee mappings must refer to registered members of this workspace.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM projects WHERE workspace_id = {workspace} AND id = {project} FOR UPDATE", ct);
        if (await db.Set<GitHubConnectionEntity>().AnyAsync(c => c.WorkspaceId == workspace && c.ProjectId == project, ct))
            throw new ApiError(409, "This project already has a GitHub connector. Use a separate Kanbada project for Jira.");
        var c = await Find(workspace, project, ct);
        if ((c?.Version ?? 0) != input.Version) throw new ApiError(409, "Jira settings changed. Reload before saving.");
        var url = policy.Validate(input.BaseUrl, input.Edition).AbsoluteUri;
        if (c is not null && (c.BaseUrl != url || c.Edition != input.Edition || c.Email != input.Email.Trim()) && string.IsNullOrWhiteSpace(input.Token))
            throw new ApiError(400, "Enter a new token when changing the Jira instance or account.");
        if (c is not null && (c.BaseUrl != url || c.Edition != input.Edition || c.JiraProjectKey != input.JiraProjectKey)
            && await db.Set<JiraLinkEntity>().AnyAsync(l => l.ConnectionId == c.Id, ct))
            throw new ApiError(400, "The Jira instance, edition and project cannot change after items have been linked. Use a new Kanbada project.");
        if (c is null)
        {
            if (string.IsNullOrWhiteSpace(input.Token)) throw new ApiError(400, "A Jira token is required.");
            c = new JiraConnectionEntity { Id = Guid.NewGuid(), WorkspaceId = workspace, ProjectId = project };
            db.Add(c);
        }
        else c.Version++;
        if (c.Direction != input.Direction)
            await ExternalCardPolicy.InvalidateWorkspace(db, workspace, ct);
        c.BaseUrl = url;
        c.Edition = input.Edition;
        c.Email = input.Email.Trim();
        if (!string.IsNullOrWhiteSpace(input.Token)) c.ProtectedToken = secrets.Protect(input.Token);
        c.Jql = input.Jql.Trim();
        c.JiraProjectKey = input.JiraProjectKey;
        c.IssueTypeId = input.IssueTypeId;
        c.Direction = input.Direction;
        c.Cron = input.Cron.Trim();
        c.TimeZone = input.TimeZone;
        c.Enabled = input.Enabled;
        c.SyncAssignees = input.SyncAssignees;
        c.ImportMissingAssignees = input.ImportMissingAssignees;
        c.NextRunAt = JiraSchedule.Next(c.Cron, c.TimeZone, DateTimeOffset.UtcNow);
        foreach (var old in c.Mappings.ToList())
        {
            var next = input.Mappings.SingleOrDefault(m => m.Kind == old.Kind && m.JiraValue == old.JiraValue);
            if (next is null) { db.Remove(old); c.Mappings.Remove(old); }
            else { old.KanbadaValue = next.KanbadaValue; old.IsDefault = next.IsDefault; }
        }
        foreach (var next in input.Mappings.Where(m => !c.Mappings.Any(old => old.Kind == m.Kind && old.JiraValue == m.JiraValue)))
            c.Mappings.Add(new JiraMappingEntity { ConnectionId = c.Id, Kind = next.Kind, KanbadaValue = next.KanbadaValue, JiraValue = next.JiraValue, IsDefault = next.IsDefault });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
