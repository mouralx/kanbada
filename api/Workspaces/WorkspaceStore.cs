using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kanbada.Api;

public sealed class WorkspaceStore(KanbadaDbContext db, WorkspaceMapper mapper)
{
    public async Task<(JsonObject State, long Version, Guid Owner, bool Personal)> Read(Guid id, Guid user, bool shared = false)
    {
        // A repeatable-read transaction keeps the separately queried collections at the same version.
        var ownTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
        var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == id && (shared || db.Members.Any(m => m.WorkspaceId == id && m.UserId == user)))
            ?? throw new ApiError(404, "Workspace not found or access denied.");
        await mapper.Load(id);
        var state = mapper.Read(workspace, user);
        await mapper.AddAttachmentMetadata(state, id);
        if (transaction is not null) await transaction.CommitAsync();
        return (state, workspace.Version, workspace.OwnerId, workspace.Personal);
    }

    public async Task<JsonArray> List(Guid user)
    {
        var workspaces = await db.Workspaces.AsNoTracking().Where(x => db.Members.Any(m => m.WorkspaceId == x.Id && m.UserId == user))
            .OrderByDescending(x => x.Personal && x.OwnerId == user).ThenBy(x => x.UpdatedAt).ToListAsync();
        return new JsonArray(workspaces.Select(x => (JsonNode)WorkspaceMapper.Summary(x, user)).ToArray());
    }

    public async Task<Guid> Create(Guid user, string name, bool personal = false)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 80) throw new ApiError(400, "Workspace name must be 1 to 80 characters.");
        var profile = await db.Users.SingleOrDefaultAsync(x => x.Id == user) ?? throw new ApiError(401, "Sign in required.");
        if (await db.Workspaces.AnyAsync(x => x.Name.ToLower() == name.ToLower() && db.Members.Any(m => m.WorkspaceId == x.Id && m.UserId == user)))
            throw new ApiError(409, "A workspace with that name already exists.");
        var id = Guid.NewGuid();
        db.Workspaces.Add(new WorkspaceEntity { Id = id, OwnerId = user, Personal = personal, Version = 1, UpdatedAt = DateTimeOffset.UtcNow });
        var initial = Initial.State(personal ? "studio" : id.ToString(), personal ? "My Workspace" : name, user, profile.Name, profile.Email, personal);
        initial["members"]![0]!["photo"] = profile.Photo;
        mapper.Apply(id, initial);
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<JsonObject> Save(Guid id, Guid user, JsonObject state, long expected)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var old = await Read(id, user);
        if (old.Version != expected)
            throw new ApiError(409, "This workspace changed. Refresh before saving to avoid overwriting someone else's changes.");
        WorkspaceValidator.Validate(state, old.Personal);
        var previousMembers = WorkspaceJson.Items(old.State, "members");
        var nextMembers = WorkspaceJson.Items(state, "members");
        var actor = previousMembers.FirstOrDefault(m => WorkspaceJson.Text(m, "userId") == user.ToString()) ?? throw new ApiError(403, "Membership required.");
        if (old.Owner != user)
        {
            if (previousMembers.Count != nextMembers.Count)
                throw new ApiError(403, "Only the owner can manage members.");
            foreach (var m in previousMembers)
            {
                var next = nextMembers.FirstOrDefault(n => WorkspaceJson.Text(n, "email") == WorkspaceJson.Text(m, "email"));
                if (next is null)
                    throw new ApiError(403, "Only the owner can manage members.");
                if (WorkspaceJson.Text(m, "userId") != user.ToString())
                {
                    var a = m!.DeepClone();
                    var b = next.DeepClone();
                    a.AsObject().Remove("invitationToken");
                    b.AsObject().Remove("invitationToken");
                    if (!JsonNode.DeepEquals(a, b))
                        throw new ApiError(403, "You can only edit your own profile.");
                }
            }

            var previousWorkspace = old.State["workspace"]!.DeepClone();
            var nextWorkspace = state["workspace"]!.DeepClone();
            if (!JsonNode.DeepEquals(previousWorkspace, nextWorkspace))
                throw new ApiError(403, "Only the owner can change workspace settings.");
        }

        var ownerMember = previousMembers.First(m => WorkspaceJson.Text(m, "userId") == old.Owner.ToString());
        if (!nextMembers.Any(m => WorkspaceJson.Text(m, "email") == WorkspaceJson.Text(ownerMember, "email")))
            throw new ApiError(400, "The workspace owner cannot be removed.");
        foreach (var m in nextMembers)
        {
            var email = WorkspaceJson.Text(m, "email").ToLowerInvariant();
            var previous = previousMembers.FirstOrDefault(n => WorkspaceJson.Text(n, "email").Equals(email, StringComparison.OrdinalIgnoreCase));
            if (previous != null)
            {
                m!["userId"] = previous["userId"]?.DeepClone();
                m["invitationToken"] = previous["invitationToken"]?.DeepClone();
            }
            else
            {
                m!["userId"] = null;
                m["invitationToken"] = Auth.Token();
            }


        }

        var self = nextMembers.FirstOrDefault(member => WorkspaceJson.Text(member, "userId") == user.ToString());
        if (self is not null)
        {
            var account = await db.Users.SingleAsync(x => x.Id == user);
            var photo = self["photo"]?.GetValue<string>();
            if (account.PhotoRequired || !string.IsNullOrEmpty(photo)) AccountAvatar.Validate(photo);
            account.Photo = photo;
        }
        if (self is not null && WorkspaceJson.Text(self, "name") != WorkspaceJson.Text(actor, "name"))
        {
            var profile = await db.Users.SingleAsync(x => x.Id == user);
            profile.Name = WorkspaceJson.Text(self, "name");
        }

        var taskIds = new HashSet<string>();
        var memberNames = nextMembers.Select(m => WorkspaceJson.Text(m, "name")).ToHashSet();
        foreach (var task in WorkspaceJson.Items(state, "tasks"))
        {
            var tid = WorkspaceJson.Text(task, "id").ToUpperInvariant();
            if (!Regex.IsMatch(tid, "^KB-[A-Z0-9-]+$") || !taskIds.Add(tid))
                throw new ApiError(400, "Card IDs must be unique uppercase identifiers.");
            task!["id"] = tid;
            foreach (var assignee in WorkspaceJson.Items(task, "assignees"))
                if (!memberNames.Contains(assignee!.GetValue<string>()))
                    throw new ApiError(400, "Unknown assignee.");
            foreach (var file in WorkspaceJson.Items(task, "attachments"))
            {
                if (!Guid.TryParse(WorkspaceJson.Text(file, "id"), out var fileId))
                    throw new ApiError(400, "Invalid file reference.");
                if (!await db.Files.AnyAsync(x => x.Id == fileId && x.WorkspaceId == id))
                    throw new ApiError(400, "A card references an unavailable file.");
            }

            var before = WorkspaceJson.Items(old.State, "tasks").FirstOrDefault(t => WorkspaceJson.Text(t, "id") == tid);
            task["history"] = CardHistory.Record(before, task, WorkspaceJson.Text(actor, "name"));
        }


        var previousFiles = WorkspaceJson.Items(old.State, "tasks").SelectMany(t => WorkspaceJson.Items(t, "attachments")).Select(a => WorkspaceJson.Text(a, "id")).ToHashSet();
        var keptFiles = WorkspaceJson.Items(state, "tasks").SelectMany(t => WorkspaceJson.Items(t, "attachments")).Select(a => WorkspaceJson.Text(a, "id")).ToHashSet();
        // Invitation tokens are hidden from non-owners in the transport response, but must remain stored.
        foreach (var member in WorkspaceJson.Items(state, "members"))
        {
            var stored = db.Members.Local.SingleOrDefault(x => x.WorkspaceId == id && x.Email == WorkspaceJson.Text(member, "email").ToLowerInvariant());
            if (stored is not null) member!["invitationToken"] = stored.InviteToken;
        }
        mapper.Apply(id, state);
        var workspace = db.Workspaces.Local.Single(x => x.Id == id);
        workspace.Version++;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var removedFiles = previousFiles.Except(keptFiles).Select(Guid.Parse).ToArray();
        await db.Files.Where(x => x.WorkspaceId == id && removedFiles.Contains(x.Id)).ExecuteDeleteAsync();
        await tx.CommitAsync();
        await tx.DisposeAsync();
        return (await Read(id, user)).State;
    }

    public async Task Delete(Guid id, Guid user)
    {
        var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == id && db.Members.Any(m => m.WorkspaceId == id && m.UserId == user))
            ?? throw new ApiError(404, "Workspace not found or access denied.");
        if (workspace.OwnerId != user) throw new ApiError(403, "Only the owner can delete a workspace.");
        if (workspace.Personal) throw new ApiError(400, "My Workspace cannot be deleted.");
        db.Workspaces.Remove(workspace);
        await db.SaveChangesAsync();
    }
}
