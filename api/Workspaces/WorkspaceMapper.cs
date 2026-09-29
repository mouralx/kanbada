using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kanbada.Api;

/// <summary>Maps the portal's workspace contract to tracked relational rows. JSON is a transport format only.</summary>
public sealed class WorkspaceMapper(KanbadaDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private HashSet<string> readOnlyCardIds = [];
    private static JsonObject Json(object value) => JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();
    private static JsonArray Array<T>(IEnumerable<T> values) => new(values.Select(x => JsonSerializer.SerializeToNode(x, JsonOptions)).ToArray());
    private static string Text(JsonNode? value, string key) => WorkspaceJson.Text(value, key);
    private static string? Optional(JsonNode? value, string key) => value?[key]?.GetValue<string>();
    private static bool Flag(JsonNode? value, string key) => value?[key]?.GetValue<bool>() ?? false;

    public async Task Load(Guid id, string[]? cardIds = null, bool sharedNotificationsOnly = false)
    {
        readOnlyCardIds = (await ExternalCardPolicy.ReadOnlyCardIds(db, id).Where(x => cardIds == null || cardIds.Contains(x)).ToListAsync()).ToHashSet();
        await db.Set<MemberEntity>().Where(x => x.WorkspaceId == id).LoadAsync();
        await db.Set<ProjectEntity>().Where(x => x.WorkspaceId == id).LoadAsync();
        await db.Set<StatusEntity>().Where(x => x.WorkspaceId == id).LoadAsync();
        await db.Set<BucketEntity>().Where(x => x.WorkspaceId == id).LoadAsync();
        await db.Set<LabelEntity>().Where(x => x.WorkspaceId == id).LoadAsync();
        await db.Set<SwimlaneEntity>().Where(x => x.WorkspaceId == id).LoadAsync();
        await db.Set<CardEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.Id))).LoadAsync();
        await db.Set<CardLabelEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.CardId))).LoadAsync();
        await db.Set<CardAssigneeEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.CardId))).LoadAsync();
        await db.Set<CardAttachmentEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.CardId))).LoadAsync();
        await db.Set<CardCommentEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.CardId))).LoadAsync();
        await db.Set<ChecklistItemEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.CardId))).LoadAsync();
        await db.Set<HistoryEntryEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.CardId))).LoadAsync();
        await db.Set<HistoryChangeEntity>().Where(x => x.WorkspaceId == id && (cardIds == null || cardIds.Contains(x.CardId))).LoadAsync();
        await db.Set<ActivityEntity>().Where(x => x.WorkspaceId == id).LoadAsync();
        await db.Set<NotificationEntity>().Where(x => x.WorkspaceId == id && (!sharedNotificationsOnly || x.RecipientId == null)).LoadAsync();
    }

    public JsonObject Read(WorkspaceEntity workspace, Guid user)
    {
        // Local/Entry normally trigger change detection. Materializing a response does not change entities.
        var detectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try { return ReadCore(workspace, user); }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detectChanges; }
    }

    private JsonObject ReadCore(WorkspaceEntity workspace, Guid user)
    {
        var id = workspace.Id;
        IEnumerable<T> Rows<T>() where T : class => db.Set<T>().Local.Where(x => (Guid)db.Entry(x).Property("WorkspaceId").CurrentValue! == id && db.Entry(x).State != EntityState.Deleted);
        var members = Rows<MemberEntity>().OrderBy(x => x.Position).ToArray();
        var statuses = Rows<StatusEntity>().ToDictionary(x => x.Id);
        var buckets = Rows<BucketEntity>().ToDictionary(x => x.Id);
        var lanes = Rows<SwimlaneEntity>().ToDictionary(x => x.Id);
        var labels = Rows<LabelEntity>().ToDictionary(x => x.Id);
        var memberNames = members.ToDictionary(x => x.Email, x => x.Name);
        // Build each association index once; never rescan a workspace-wide collection per card/history entry.
        var cardLabels = Rows<CardLabelEntity>().OrderBy(x => x.Position).ToLookup(x => x.CardId);
        var assignees = Rows<CardAssigneeEntity>().OrderBy(x => x.Position).ToLookup(x => x.CardId);
        var comments = Rows<CardCommentEntity>().OrderBy(x => x.Position).ToLookup(x => x.CardId);
        var checklists = Rows<ChecklistItemEntity>().OrderBy(x => x.Position).ToLookup(x => x.CardId);
        var histories = Rows<HistoryEntryEntity>().OrderBy(x => x.Position).ToLookup(x => x.CardId);
        var historyChanges = Rows<HistoryChangeEntity>().OrderBy(x => x.Position).ToLookup(x => (x.CardId, x.HistoryId));
        return new JsonObject
        {
            ["version"] = workspace.Version,
            ["workspace"] = Summary(workspace, user),
            ["members"] = Array(members.Select(x => new { x.Name, x.Email, x.Initials, x.Color, x.Photo, x.UserId, invitationToken = workspace.OwnerId == user ? x.InviteToken : null })),
            ["projects"] = Array(Rows<ProjectEntity>().OrderBy(x => x.Position).Select(x => new { x.Id, x.Name, x.Color, x.Description, x.Archived, x.System })),
            ["statuses"] = Array(Rows<StatusEntity>().OrderBy(x => x.Position).Select(x => new { x.Id, x.Name, x.Color, x.Complete })),
            ["buckets"] = Array(Rows<BucketEntity>().OrderBy(x => x.Position).Select(x => new { x.Id, x.Name, x.Color, x.Complete })),
            ["labels"] = Array(Rows<LabelEntity>().OrderBy(x => x.Position).Select(x => new { x.Id, x.Name, x.Color, x.Complete })),
            ["swimlanes"] = Array(Rows<SwimlaneEntity>().OrderBy(x => x.Position).Select(x => new { x.Id, x.Name, x.Color, x.Complete, project = x.ProjectId })),
            ["activity"] = Array(Rows<ActivityEntity>().OrderBy(x => x.Position).Select(x => x.Text)),
            ["notifications"] = new JsonArray(Rows<NotificationEntity>().Where(x => x.RecipientId is null || x.RecipientId == user)
                .OrderBy(x => x.Position).Select(x =>
                {
                    var notification = Json(new { x.Id, x.Message, x.At });
                    if (x.CardId is not null) notification["cardId"] = x.CardId;
                    return (JsonNode)notification;
                }).ToArray()),
            ["tasks"] = new JsonArray(Rows<CardEntity>().OrderBy(x => x.Position).Select(card => (JsonNode)new JsonObject
            {
                ["id"] = card.Id,
                ["readOnly"] = readOnlyCardIds.Contains(card.Id),
                ["project"] = card.ProjectId,
                ["title"] = card.Title,
                ["description"] = card.Description,
                ["status"] = statuses[card.StatusId].Name,
                ["priority"] = card.Priority,
                ["due"] = card.Due?.ToString("yyyy-MM-dd") ?? "",
                ["bucket"] = card.BucketId is null ? "" : buckets[card.BucketId].Name,
                ["swimlane"] = card.SwimlaneId is null ? "" : lanes[card.SwimlaneId].Name,
                ["cover"] = card.Cover,
                ["labels"] = Array(cardLabels[card.Id].Select(x => labels[x.LabelId].Name)),
                ["assignees"] = Array(assignees[card.Id].Select(x => memberNames[x.MemberEmail])),
                ["comments"] = Array(comments[card.Id].Select(x => x.Text)),
                ["checklist"] = Array(checklists[card.Id].Select(x => new { x.Text, x.Done })),
                ["history"] = new JsonArray(histories[card.Id].Select(x => (JsonNode)new JsonObject
                {
                    ["id"] = x.Id,
                    ["at"] = x.At.ToString("O"),
                    ["actor"] = x.Actor,
                    ["changes"] = Array(historyChanges[(card.Id, x.Id)].Select(c => c.Text))
                }).ToArray()),
                ["attachments"] = new JsonArray()
            }).ToArray())
        };
    }

    public static JsonObject Summary(WorkspaceEntity x, Guid user) => Json(new
    {
        id = x.Personal && x.OwnerId == user ? "studio" : x.Id.ToString(),
        x.Name,
        x.Icon,
        x.Banner,
        x.BannerPosition,
        x.Personal,
        x.OwnerId,
        canManage = x.OwnerId == user
    });

    public async Task AddAttachmentMetadata(JsonObject state, Guid id)
    {
        // Project metadata only: loading a board never loads the binary documents.
        var fileIds = db.CardAttachments.Local.Where(x => x.WorkspaceId == id).Select(x => x.FileId).Distinct().ToArray();
        var files = await db.Files.Where(x => x.WorkspaceId == id && fileIds.Contains(x.Id)).Select(x => new { x.Id, x.Name, size = x.Bytes.Length, type = x.ContentType, addedAt = x.CreatedAt }).ToDictionaryAsync(x => x.Id);
        var detectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var attachments = db.CardAttachments.Local.Where(x => x.WorkspaceId == id).OrderBy(x => x.Position).ToLookup(x => x.CardId);
            foreach (var card in WorkspaceJson.Items(state, "tasks"))
                card!["attachments"] = Array(attachments[Text(card, "id")].Select(x => files[x.FileId]));
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detectChanges; }
    }

    private void SyncNotifications(Guid workspace, JsonArray incoming, Guid? user)
    {
        var personal = db.Notifications.Local.Where(n => n.WorkspaceId == workspace && n.RecipientId != null)
            .ToDictionary(n => n.Id);
        foreach (var item in incoming)
        {
            var id = Text(item, "id");
            if (personal.TryGetValue(id, out var notification))
            {
                if (notification.RecipientId != user)
                    throw new ApiError(403, "You cannot change another member's notifications.");
                if (Text(item, "message") != notification.Message || Text(item, "at") != notification.At
                    || Optional(item, "cardId") != notification.CardId)
                    throw new ApiError(400, "Assignment notifications can only be dismissed.");
            }
            else if (id.StartsWith("assignment-", StringComparison.Ordinal) || Optional(item, "cardId") is not null)
                throw new ApiError(400, "Assignment notifications are created by the server.");
        }
        var retained = incoming.Select(n => Text(n, "id")).ToHashSet();
        Sync(workspace, incoming.Where(n => !personal.ContainsKey(Text(n, "id"))).Select((x, position) =>
            new NotificationEntity { WorkspaceId = workspace, Position = position, Id = Text(x, "id"), Message = Text(x, "message"), At = Text(x, "at") })
            .Concat(personal.Values.Where(n => n.RecipientId != user || retained.Contains(n.Id))));
    }

    public void Apply(Guid id, JsonObject state, Guid? user = null, int? nextPosition = null)
    {
        // SetValues marks changed properties itself. Defer the graph scan until SaveChangesAsync.
        var detectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
        var cascadeTiming = db.ChangeTracker.CascadeDeleteTiming;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        // Parents and their removed dependents are reconciled in the same pass.
        db.ChangeTracker.CascadeDeleteTiming = Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming.OnSaveChanges;
        try { ApplyCore(id, state, user, nextPosition); }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
            db.ChangeTracker.CascadeDeleteTiming = cascadeTiming;
        }
    }

    private void ApplyCore(Guid id, JsonObject state, Guid? user, int? nextPosition)
    {
        var workspace = db.Workspaces.Local.Single(x => x.Id == id);
        workspace.Name = Text(state["workspace"], "name");
        workspace.Icon = Optional(state["workspace"], "icon");
        workspace.Banner = Optional(state["workspace"], "banner");
        workspace.BannerPosition = state["workspace"]?["bannerPosition"]?.GetValue<double>();
        Sync(id, WorkspaceJson.Items(state, "members").Select((x, position) => new MemberEntity { WorkspaceId = id, Position = position, Email = Text(x, "email").ToLowerInvariant(), UserId = Guid.TryParse(Text(x, "userId"), out var uid) ? uid : null, InviteToken = Optional(x, "invitationToken"), Name = Text(x, "name"), Initials = Text(x, "initials"), Color = Text(x, "color"), Photo = Optional(x, "photo") }));
        Sync(id, WorkspaceJson.Items(state, "projects").Select((x, position) => new ProjectEntity { WorkspaceId = id, Position = position, Id = Text(x, "id"), Name = Text(x, "name"), Color = Text(x, "color"), Description = Text(x, "description"), Archived = Flag(x, "archived"), System = Optional(x, "system") }));
        Sync(id, WorkspaceJson.Items(state, "statuses").Select((x, position) => new StatusEntity { WorkspaceId = id, Position = position, Id = Text(x, "id"), Name = Text(x, "name"), Color = Text(x, "color"), Complete = Flag(x, "complete") }));
        Sync(id, WorkspaceJson.Items(state, "buckets").Select((x, position) => new BucketEntity { WorkspaceId = id, Position = position, Id = Text(x, "id"), Name = Text(x, "name"), Color = Text(x, "color"), Complete = Flag(x, "complete") }));
        Sync(id, WorkspaceJson.Items(state, "labels").Select((x, position) => new LabelEntity { WorkspaceId = id, Position = position, Id = Text(x, "id"), Name = Text(x, "name"), Color = Text(x, "color"), Complete = Flag(x, "complete") }));
        Sync(id, WorkspaceJson.Items(state, "swimlanes").Select((x, position) => new SwimlaneEntity { WorkspaceId = id, Position = position, Id = Text(x, "id"), ProjectId = Text(x, "project"), Name = Text(x, "name"), Color = Text(x, "color"), Complete = Flag(x, "complete") }));
        Sync(id, WorkspaceJson.Items(state, "activity").Select((x, position) => new ActivityEntity { WorkspaceId = id, Position = position, Text = x!.GetValue<string>() }));
        SyncNotifications(id, WorkspaceJson.Items(state, "notifications"), user);
        string Definition(string collection, string name) => Text(WorkspaceJson.Items(state, collection).Single(x => Text(x, "name") == name), "id");
        var cards = WorkspaceJson.Items(state, "tasks");
        var positions = db.Cards.Local.Where(x => x.WorkspaceId == id).ToDictionary(x => x.Id, x => x.Position);
        var next = nextPosition.GetValueOrDefault();
        int Position(string cardId, int position) => nextPosition is null ? position
            : positions.TryGetValue(cardId, out var existing) ? existing : next++;
        Sync(id, cards.Select((x, position) => new CardEntity
        {
            WorkspaceId = id,
            Id = Text(x, "id"),
            Position = Position(Text(x, "id"), position),
            ProjectId = Text(x, "project"),
            StatusId = Definition("statuses", Text(x, "status")),
            BucketId = Text(x, "bucket") == "" ? null : Definition("buckets", Text(x, "bucket")),
            SwimlaneId = Text(x, "swimlane") == "" ? null : Text(WorkspaceJson.Items(state, "swimlanes").Single(l => Text(l, "name") == Text(x, "swimlane") && Text(l, "project") == Text(x, "project")), "id"),
            Title = Text(x, "title"),
            Description = Text(x, "description"),
            Priority = Text(x, "priority"),
            Due = Text(x, "due") == "" ? null : DateOnly.ParseExact(Text(x, "due"), "yyyy-MM-dd"),
            Cover = Optional(x, "cover")
        }));
        Sync(id, cards.SelectMany(card => WorkspaceJson.Items(card, "labels").Select((x, position) => new CardLabelEntity { WorkspaceId = id, CardId = Text(card, "id"), Position = position, LabelId = Definition("labels", x!.GetValue<string>()) })));
        Sync(id, cards.SelectMany(card => WorkspaceJson.Items(card, "assignees").Select((x, position) => new CardAssigneeEntity { WorkspaceId = id, CardId = Text(card, "id"), Position = position, MemberEmail = Text(WorkspaceJson.Items(state, "members").Single(m => Text(m, "name") == x!.GetValue<string>()), "email").ToLowerInvariant() })));
        Sync(id, cards.SelectMany(card => WorkspaceJson.Items(card, "attachments").Select((x, position) => new CardAttachmentEntity { WorkspaceId = id, CardId = Text(card, "id"), Position = position, FileId = Guid.Parse(Text(x, "id")) })));
        Sync(id, cards.SelectMany(card => WorkspaceJson.Items(card, "comments").Select((x, position) => new CardCommentEntity { WorkspaceId = id, CardId = Text(card, "id"), Position = position, Text = x!.GetValue<string>() })));
        Sync(id, cards.SelectMany(card => WorkspaceJson.Items(card, "checklist").Select((x, position) => new ChecklistItemEntity { WorkspaceId = id, CardId = Text(card, "id"), Position = position, Text = Text(x, "text"), Done = Flag(x, "done") })));
        Sync(id, cards.SelectMany(card => WorkspaceJson.Items(card, "history").Select((x, position) => new HistoryEntryEntity { WorkspaceId = id, CardId = Text(card, "id"), Position = position, Id = Text(x, "id"), At = DateTimeOffset.Parse(Text(x, "at")).ToUniversalTime(), Actor = Text(x, "actor") })));
        Sync(id, cards.SelectMany(card => WorkspaceJson.Items(card, "history").SelectMany(history => WorkspaceJson.Items(history, "changes").Select((x, position) => new HistoryChangeEntity
        {
            WorkspaceId = id,
            CardId = Text(card, "id"),
            HistoryId = Text(history, "id"),
            Position = position,
            Text = x!.GetValue<string>()
        }))));
    }

    // Keep existing tracked rows, update changed scalar values, add new keys and remove missing keys.
    // EF orders commands using the configured foreign keys and writes only actual changes.
    private void Sync<T>(Guid workspace, IEnumerable<T> incoming) where T : class
    {
        var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        string Key(T entity) => string.Join("\0", key.Properties.Select(p => p.PropertyInfo!.GetValue(entity)?.ToString()));
        var current = db.Set<T>().Local.Where(x => (Guid)db.Entry(x).Property("WorkspaceId").CurrentValue! == workspace).ToDictionary(Key);
        foreach (var entity in incoming)
        {
            if (current.Remove(Key(entity), out var tracked)) db.Entry(tracked).CurrentValues.SetValues(entity);
            else db.Add(entity);
        }
        db.RemoveRange(current.Values);
    }
}
