using Kanbada.Api;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;

if (args.Length != 2 || args[0] is not ("--reset-keep-user" or "--verify-user"))
    throw new ArgumentException("Usage from repository root: dotnet run --project tools/Kanbada.LoadData -- --reset-keep-user EMAIL. Deletes existing workspace data and all other accounts. Stop the API and back up first.");

await using var db = new KanbadaDbContextFactory().CreateDbContext([]);
db.Database.SetCommandTimeout(300);
var email = args[1].Trim().ToLowerInvariant();
var owner = await db.Users.AsNoTracking().SingleAsync(x => x.Email == email);
if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Apply migrations before seeding.");
if (args[0] == "--verify-user")
{
    var mapper = new WorkspaceMapper(db);
    var store = new WorkspaceStore(db, mapper);
    var workspaces = await store.List(owner.Id);
    Console.WriteLine($"Accessible workspaces: {workspaces.Count}");
    var metrics = new WorkspaceMetrics(db);
    foreach (var workspace in await db.Workspaces.AsNoTracking().OrderBy(x => x.UpdatedAt).ToListAsync())
    {
        var result = await metrics.Calculate(workspace.Id, owner.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        Console.WriteLine($"{workspace.Name}: {System.Text.Json.JsonSerializer.Serialize(result)}");
    }
    foreach (var workspace in await db.Workspaces.AsNoTracking().OrderBy(x => x.UpdatedAt).ToListAsync())
    {
        db.ChangeTracker.Clear();
        var timer = Stopwatch.StartNew();
        var snapshot = await store.Read(workspace.Id, owner.Id);
        var readSeconds = timer.Elapsed.TotalSeconds;
        WorkspaceValidator.Validate(snapshot.State, workspace.Personal);
        var bytes = Encoding.UTF8.GetByteCount(snapshot.State.ToJsonString());
        Console.WriteLine($"{workspace.Name}: {WorkspaceJson.Items(snapshot.State, "tasks").Count} cards, read {readSeconds:F2}s, read/validation/serialization {timer.Elapsed.TotalSeconds:F2}s, {bytes:N0} response bytes.");
    }
    return;
}
var clock = Stopwatch.StartNew();
await using var transaction = await db.Database.BeginTransactionAsync();
// Cascades remove workspace-owned records; only the retained user's authentication records survive.
await db.Workspaces.ExecuteDeleteAsync();
await db.Users.Where(x => x.Id != owner.Id).ExecuteDeleteAsync();
db.ChangeTracker.Clear();
var now = DateTimeOffset.UtcNow;
string[] colors = ["#4f8bd6", "#7ba6d9", "#87b4ac", "#c3a36a", "#9b8ac4", "#cf899a", "#7597b2", "#90a776"];
string[] statuses = ["Backlog", "Ready", "In progress", "In review", "Blocked", "Done"];
string[] buckets = ["Discovery", "Design", "Engineering", "Quality", "Security", "Operations", "Customer feedback", "Documentation", "Infrastructure", "Release"];
string[] labels = ["Customer", "Performance", "Accessibility", "Security", "Compliance", "Reliability", "Automation", "Mobile", "Analytics", "Integration", "Research", "Technical debt", "Documentation", "Urgent", "Enhancement", "Regression", "API", "Portal", "Database", "Monitoring"];
string[] subjects = ["Improve onboarding", "Optimize dashboard queries", "Review access controls", "Automate release validation", "Investigate customer feedback", "Refine notification delivery", "Validate document uploads", "Measure board responsiveness", "Harden integration contracts", "Upgrade operational reporting", "Review accessibility", "Expand audit coverage"];
string[] projectNames = ["My activities", "Customer Experience", "Platform Reliability", "Product Discovery", "Service Operations", "Identity and Access", "Mobile Experience", "Data Platform", "Enterprise Integrations", "Quality Engineering", "Growth Experiments", "Design System", "Developer Experience", "Release Engineering", "Security Programme", "Infrastructure", "Reporting and Insights", "Customer Success", "Legacy Migration", "Previous Quarter"];
var sizes = new[] { 500, 2500, 5000, 5000, 7000 };
string[] workspaceNames = ["My Workspace", "Product & Engineering", "Delivery Portfolio", "Service Operations", "Enterprise Scale"];
for (var w = 0; w < sizes.Length; w++)
{
    var workspace = Guid.NewGuid();
    db.Workspaces.Add(new WorkspaceEntity { Id = workspace, OwnerId = owner.Id, Name = workspaceNames[w], Personal = w == 0, Version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), UpdatedAt = now.AddSeconds(w) });
    db.Members.Add(new MemberEntity { WorkspaceId = workspace, Email = owner.Email, UserId = owner.Id, Name = owner.Name, Initials = string.Concat(owner.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0]))), Color = colors[w], Position = 0 });
    for (var s = 0; s < statuses.Length; s++) db.Statuses.Add(new StatusEntity { WorkspaceId = workspace, Id = "status-" + s, Name = statuses[s], Complete = s == 5, Color = colors[s], Position = s });
    for (var b = 0; b < buckets.Length; b++) db.Buckets.Add(new BucketEntity { WorkspaceId = workspace, Id = "bucket-" + b, Name = buckets[b], Color = colors[b % colors.Length], Position = b });
    for (var l = 0; l < labels.Length; l++) db.Labels.Add(new LabelEntity { WorkspaceId = workspace, Id = "label-" + l, Name = labels[l], Color = colors[l % colors.Length], Position = l });
    for (var p = 0; p < projectNames.Length; p++)
    {
        var project = p == 0 ? "my-activities" : "project-" + p;
        db.Projects.Add(new ProjectEntity { WorkspaceId = workspace, Id = project, Name = projectNames[p], Description = $"{projectNames[p]} portfolio: delivery milestones, service improvements, customer outcomes and operational follow-up.", Color = colors[p % colors.Length], Archived = p >= 18, System = p == 0 ? "activities" : null, Position = p });
        for (var lane = 0; lane < 4; lane++) db.Swimlanes.Add(new SwimlaneEntity { WorkspaceId = workspace, Id = $"lane-{p}-{lane}", ProjectId = project, Name = new[] { "Expedite", "Standard delivery", "Continuous improvement", "Scheduled work" }[lane], Color = colors[lane], Position = p * 4 + lane });
    }
    for (var n = 0; n < 100; n++) db.Notifications.Add(new NotificationEntity { WorkspaceId = workspace, Id = Guid.NewGuid().ToString("N"), Message = $"{subjects[n % subjects.Length]} · portfolio update {n + 1}", At = now.AddMinutes(-n * 37).ToString("O"), Position = n });
    for (var n = 0; n < 30; n++) db.Activities.Add(new ActivityEntity { WorkspaceId = workspace, Position = n, Text = $"{subjects[n % subjects.Length]} · {statuses[n % statuses.Length]}" });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    for (var start = 0; start < sizes[w]; start += 250)
    {
        for (var i = start; i < Math.Min(start + 250, sizes[w]); i++)
        {
            var card = $"KB-{w + 1:D2}-{i + 1:D6}";
            var p = i % projectNames.Length;
            var status = (i / projectNames.Length + i / 7) % statuses.Length;
            db.Cards.Add(new CardEntity
            {
                WorkspaceId = workspace,
                Id = card,
                ProjectId = p == 0 ? "my-activities" : "project-" + p,
                StatusId = "status-" + status,
                BucketId = i % 11 == 0 ? null : "bucket-" + ((i / 3) % buckets.Length),
                SwimlaneId = i % 9 == 0 ? null : $"lane-{p}-{(i / 20) % 4}",
                Title = $"{subjects[i % subjects.Length]} · {i + 1:D4}",
                Description = $"Objective\nDeliver a measurable improvement for {projectNames[p]} within {workspaceNames[w]}.\n\nAcceptance criteria\nValidate functionality, measure latency and document the outcome. Include accessibility, security and recovery checks.\n\nContext\nWork item {card} represents a realistic portfolio activity with comments, a checklist, labels and audit history.",
                Priority = new[] { "Low", "Medium", "High" }[i % 3],
                Due = i % 4 == 0 ? null : DateOnly.FromDateTime(now.UtcDateTime).AddDays((i % 91) - 35),
                Cover = i % 8 == 0 ? colors[i % colors.Length] : null,
                Position = i
            });
            if (i % 5 != 0) db.CardAssignees.Add(new CardAssigneeEntity { WorkspaceId = workspace, CardId = card, MemberEmail = owner.Email });
            for (var l = 0; l < 2; l++) db.CardLabels.Add(new CardLabelEntity { WorkspaceId = workspace, CardId = card, LabelId = "label-" + ((i + l * 7) % labels.Length), Position = l });
            for (var c = 0; c < 3; c++) db.CardComments.Add(new CardCommentEntity { WorkspaceId = workspace, CardId = card, Position = c, Text = new[] { "Scope reviewed. Track the agreed acceptance criteria before starting implementation.", "Validation notes: include realistic data volumes and verify all error paths.", "Next checkpoint: capture results and update the delivery documentation." }[c] });
            for (var c = 0; c < 5; c++) db.ChecklistItems.Add(new ChecklistItemEntity { WorkspaceId = workspace, CardId = card, Position = c, Text = new[] { "Confirm requirements", "Implement the change", "Review accessibility and security", "Validate acceptance criteria", "Document the outcome" }[c], Done = status == 5 || c < i % 5 });
            for (var h = 0; h < 4; h++)
            {
                var history = $"seed-{i}-{h}";
                db.HistoryEntries.Add(new HistoryEntryEntity { WorkspaceId = workspace, CardId = card, Id = history, Position = h, Actor = owner.Name, At = now.AddDays(-(i % 60)).AddHours(-h * 8) });
                db.HistoryChanges.Add(new HistoryChangeEntity { WorkspaceId = workspace, CardId = card, HistoryId = history, Position = 0, Text = new[] { "Reviewed acceptance criteria", "Updated delivery details", "Added checklist and labels", "Created card in Backlog" }[h] });
            }
            if (i % 20 == 0)
            {
                var file = Guid.NewGuid();
                var content = Encoding.UTF8.GetBytes($"Delivery brief for {card}\n{projectNames[p]}\n" + string.Concat(Enumerable.Repeat("Acceptance: verify correctness, responsiveness, access control and recovery.\n", 900)));
                db.Files.Add(new FileEntity { Id = file, WorkspaceId = workspace, UploaderId = owner.Id, Name = $"{card}-delivery-brief.txt", ContentType = "text/plain", Bytes = content, CreatedAt = now.AddDays(-i % 60) });
                db.CardAttachments.Add(new CardAttachmentEntity { WorkspaceId = workspace, CardId = card, FileId = file });
            }
            if (i % 100 == 0) db.Shares.Add(new ShareEntity { Token = Auth.Token(), WorkspaceId = workspace, CardId = card, CreatorId = owner.Id, Access = "members", CreatedAt = now, ExpiresAt = i % 200 == 0 ? null : now.AddDays(30) });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
    Console.WriteLine($"Seeded {workspaceNames[w]}: {sizes[w]:N0} cards");
}
await transaction.CommitAsync();
Console.WriteLine($"Completed in {clock.Elapsed.TotalSeconds:F1}s. Users: {await db.Users.CountAsync()}, workspaces: {await db.Workspaces.CountAsync()}, projects: {await db.Projects.CountAsync()}, cards: {await db.Cards.CountAsync()}, files: {await db.Files.CountAsync()}.");
