using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Kanbada.Api;

public sealed class GitHubSettings(KanbadaDbContext db, WorkspaceResolver resolver, GitHubSecrets secrets, GitHubDestinationPolicy policy, GitHubClient github)
{
    public async Task<Guid> Authorize(string workspace, string project, Guid user, CancellationToken ct)
    {
        var id = await resolver.WorkspaceId(workspace, user);
        if (!await db.Workspaces.AnyAsync(w => w.Id == id && w.OwnerId == user, ct))
            throw new ApiError(403, "Only the workspace owner can manage GitHub synchronization.");
        if (!await db.Projects.AnyAsync(p => p.WorkspaceId == id && p.Id == project, ct)) throw new ApiError(404, "Project not found.");
        return id;
    }

    public Task<GitHubConnectionEntity?> Find(Guid workspace, string project, CancellationToken ct) =>
        db.Set<GitHubConnectionEntity>().Include(c => c.Mappings).SingleOrDefaultAsync(c => c.WorkspaceId == workspace && c.ProjectId == project, ct);

    public async Task<object?> Read(Guid workspace, string project, CancellationToken ct)
    {
        var c = await Find(workspace, project, ct);
        if (c is null) return null;
        var problems = await db.Set<GitHubLinkEntity>().AsNoTracking().Where(l => l.ConnectionId == c.Id && (l.LastError != null || l.CreationPending))
            .OrderBy(l => l.CardId).Take(100).Select(l => new { l.Id, l.CardId, l.DisplayKey, l.Url, l.CreationPending, l.LastError }).ToListAsync(ct);
        return new
        {
            c.Id, c.Version, c.BaseUrl, c.Owner, c.OwnerType, c.ProjectNumber, c.Repository, HasToken = c.ProtectedToken.Length > 0,
            c.RemoteProjectId, c.RepositoryId, c.Direction, c.StatusFieldId, c.PriorityFieldId, c.DueFieldId, c.SyncLabels, c.SyncAssignees, c.ImportMissingAssignees,
            c.Cron, c.TimeZone, c.Enabled, c.NextRunAt, c.RequestedAt, c.LastStartedAt, c.LastFinishedAt, c.LastError, c.LastSyncedCount,
            Mappings = c.Mappings.Select(m => new GitHubMappingInput(m.Kind, m.KanbadaValue, m.GitHubValue, m.IsDefault)), Problems = problems
        };
    }

    public async Task<GitHubConnectionEntity> ForTest(Guid workspace, string project, GitHubConnectionInput input, CancellationToken ct)
    {
        var url = policy.Validate(input.BaseUrl).AbsoluteUri;
        var owner = input.Owner ?? "";
        if (input.OwnerType is not ("organization" or "user") || !Regex.IsMatch(owner, "^[A-Za-z0-9][A-Za-z0-9-]{0,99}$")
            || input.ProjectNumber < 1 || input.Repository is null
            || (input.Repository.Length > 0 && !Regex.IsMatch(input.Repository, "^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")))
            throw new ApiError(400, "Provide a project owner, owner type, positive project number and repository in owner/name format.");
        if (input.Direction is not ("github-to-kanbada" or "kanbada-to-github" or "bidirectional"))
            throw new ApiError(400, "Select a supported GitHub synchronization direction.");
        if (input.Direction != "github-to-kanbada" && input.Repository.Length == 0)
            throw new ApiError(400, "Choose a repository for new GitHub issues.");
        var stored = await Find(workspace, project, ct);
        var token = input.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            if (stored is null || stored.BaseUrl != url) throw new ApiError(400, "Enter a GitHub token for this server.");
            token = secrets.Unprotect(stored.ProtectedToken);
        }
        if (token.Length > 4096 || token.Any(char.IsControl)) throw new ApiError(400, "Invalid GitHub token.");
        return new GitHubConnectionEntity
        {
            BaseUrl = url, Owner = owner, OwnerType = input.OwnerType, ProjectNumber = input.ProjectNumber,
            Repository = input.Repository, Direction = input.Direction, ProtectedToken = secrets.Protect(token)
        };
    }

    public async Task Save(Guid workspace, string project, GitHubConnectionInput input, CancellationToken ct)
    {
        var candidate = await ForTest(workspace, project, input, ct);
        JiraSchedule.Next(input.Cron, input.TimeZone, DateTimeOffset.UtcNow);
        if (input.StatusFieldId is null || input.PriorityFieldId is null || input.DueFieldId is null
            || input.Mappings is null || input.Mappings.Count > 500 || input.Mappings.Any(m => m is null || m.Kind is not ("status" or "priority" or "assignee")
                || string.IsNullOrWhiteSpace(m.KanbadaValue) || m.KanbadaValue.Length > 255 || string.IsNullOrWhiteSpace(m.GitHubValue)
                || m.GitHubValue.Length > 255 || m.GitHubValue.Any(char.IsControl))
            || input.Mappings.GroupBy(m => (m.Kind, m.GitHubValue)).Any(g => g.Count() > 1)
            || input.Mappings.Where(m => m.Kind is "status" or "priority").GroupBy(m => (m.Kind, m.KanbadaValue)).Any(g => g.Count(m => m.IsDefault) != 1))
            throw new ApiError(400, "Use valid, unique GitHub mappings and exactly one default per mapped Kanbada value.");
        var metadata = await github.Metadata(candidate, ct);
        GitHubField Field(string id, string type) => metadata.Fields.SingleOrDefault(f => f.Id == id && f.DataType == type)
            ?? throw new ApiError(400, "A selected GitHub field is unavailable or has an incompatible type.");
        var status = Field(input.StatusFieldId, "SINGLE_SELECT");
        GitHubField? priority = input.PriorityFieldId.Length > 0 ? Field(input.PriorityFieldId, "SINGLE_SELECT") : null;
        if (input.PriorityFieldId == input.StatusFieldId) throw new ApiError(400, "Status and priority must use different GitHub fields.");
        if (input.DueFieldId.Length > 0) Field(input.DueFieldId, "DATE");
        var statuses = await db.Statuses.Where(s => s.WorkspaceId == workspace).Select(s => s.Id).ToListAsync(ct);
        var members = await db.Members.Where(m => m.WorkspaceId == workspace && m.UserId != null).Select(m => m.UserId!.Value.ToString()).ToListAsync(ct);
        foreach (var mapping in input.Mappings)
        {
            if (mapping.Kind == "assignee")
            {
                if (!members.Contains(mapping.KanbadaValue)) throw new ApiError(400, "Assignees must map to registered workspace members.");
                continue;
            }
            var field = mapping.Kind == "status" ? status : priority ?? throw new ApiError(400, "Choose a priority field before mapping priorities.");
            if (mapping.GitHubValue != GitHubClient.Unset && !field.Options.Any(o => o.Id == mapping.GitHubValue))
                throw new ApiError(400, "A mapped GitHub field option is unavailable.");
            if (mapping.Kind == "status" ? !statuses.Contains(mapping.KanbadaValue) : mapping.KanbadaValue is not ("Low" or "Medium" or "High"))
                throw new ApiError(400, "A mapped Kanbada status or priority is invalid.");
        }
        if (!input.Mappings.Any(m => m.Kind == "status")
            || (priority is not null && !input.Mappings.Where(m => m.Kind == "priority").Select(m => m.KanbadaValue).Distinct().Order().SequenceEqual(new[] { "High", "Low", "Medium" })))
            throw new ApiError(400, "Map at least one status and all three priorities when priority synchronization is enabled.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM projects WHERE workspace_id = {workspace} AND id = {project} FOR UPDATE", ct);
        if (await db.Set<JiraConnectionEntity>().AnyAsync(c => c.WorkspaceId == workspace && c.ProjectId == project, ct))
            throw new ApiError(409, "This project already has a Jira connector. Use a separate Kanbada project for GitHub.");
        var c = await Find(workspace, project, ct);
        if (c is not null) await db.Entry(c).ReloadAsync(ct);
        if ((c?.Version ?? 0) != input.Version) throw new ApiError(409, "GitHub settings changed. Reload before saving.");
        if (c is not null && (c.BaseUrl != candidate.BaseUrl || c.RemoteProjectId != metadata.ProjectId || (c.RepositoryId.Length > 0 && c.RepositoryId != metadata.RepositoryId))
            && await db.Set<GitHubLinkEntity>().AnyAsync(l => l.ConnectionId == c.Id, ct))
            throw new ApiError(400, "The GitHub server, project and target repository cannot change after linking items. Use a new Kanbada project.");
        if (await db.Set<GitHubConnectionEntity>().AnyAsync(x => x.WorkspaceId == workspace && x.ProjectId != project
            && x.BaseUrl == candidate.BaseUrl && x.RemoteProjectId == metadata.ProjectId, ct))
            throw new ApiError(409, "This GitHub project is already connected in the workspace.");
        if (c is null) { c = new GitHubConnectionEntity { Id = Guid.NewGuid(), WorkspaceId = workspace, ProjectId = project }; db.Add(c); }
        else c.Version++;
        if (c.Direction != input.Direction) await ExternalCardPolicy.InvalidateWorkspace(db, workspace, ct);
        c.BaseUrl = candidate.BaseUrl; c.Owner = candidate.Owner; c.OwnerType = candidate.OwnerType; c.ProjectNumber = candidate.ProjectNumber;
        c.Repository = candidate.Repository; c.RemoteProjectId = metadata.ProjectId; c.RepositoryId = metadata.RepositoryId; c.ProtectedToken = candidate.ProtectedToken;
        c.Direction = input.Direction; c.StatusFieldId = input.StatusFieldId; c.PriorityFieldId = input.PriorityFieldId; c.DueFieldId = input.DueFieldId;
        c.SyncLabels = input.SyncLabels; c.SyncAssignees = input.SyncAssignees; c.Cron = input.Cron.Trim(); c.TimeZone = input.TimeZone; c.Enabled = input.Enabled;
        c.ImportMissingAssignees = input.ImportMissingAssignees;
        c.NextRunAt = JiraSchedule.Next(c.Cron, c.TimeZone, DateTimeOffset.UtcNow);
        foreach (var old in c.Mappings.ToList())
        {
            var next = input.Mappings.SingleOrDefault(m => m.Kind == old.Kind && m.GitHubValue == old.GitHubValue);
            if (next is null) { db.Remove(old); c.Mappings.Remove(old); }
            else { old.KanbadaValue = next.KanbadaValue; old.IsDefault = next.IsDefault; }
        }
        foreach (var next in input.Mappings.Where(m => !c.Mappings.Any(old => old.Kind == m.Kind && old.GitHubValue == m.GitHubValue)))
            c.Mappings.Add(new GitHubMappingEntity { ConnectionId = c.Id, Kind = next.Kind, KanbadaValue = next.KanbadaValue, GitHubValue = next.GitHubValue, IsDefault = next.IsDefault });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
