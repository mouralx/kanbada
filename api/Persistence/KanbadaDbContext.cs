using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Kanbada.Api;

public sealed class KanbadaDbContext(DbContextOptions<KanbadaDbContext> options) : DbContext(options)
{
    public DbSet<RecoveryCodeEntity> RecoveryCodes => Set<RecoveryCodeEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<IdentityEntity> Identities => Set<IdentityEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<WorkspaceEntity> Workspaces => Set<WorkspaceEntity>();
    public DbSet<MemberEntity> Members => Set<MemberEntity>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<ShareEntity> Shares => Set<ShareEntity>();
    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<StatusEntity> Statuses => Set<StatusEntity>();
    public DbSet<BucketEntity> Buckets => Set<BucketEntity>();
    public DbSet<LabelEntity> Labels => Set<LabelEntity>();
    public DbSet<SwimlaneEntity> Swimlanes => Set<SwimlaneEntity>();
    public DbSet<CardEntity> Cards => Set<CardEntity>();
    public DbSet<CardLabelEntity> CardLabels => Set<CardLabelEntity>();
    public DbSet<CardAssigneeEntity> CardAssignees => Set<CardAssigneeEntity>();
    public DbSet<CardAttachmentEntity> CardAttachments => Set<CardAttachmentEntity>();
    public DbSet<CardCommentEntity> CardComments => Set<CardCommentEntity>();
    public DbSet<ChecklistItemEntity> ChecklistItems => Set<ChecklistItemEntity>();
    public DbSet<HistoryEntryEntity> HistoryEntries => Set<HistoryEntryEntity>();
    public DbSet<HistoryChangeEntity> HistoryChanges => Set<HistoryChangeEntity>();
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<UserEntity>(e =>
        {
            e.ToTable("users", t => t.HasCheckConstraint("users_normalized_email", "email = lower(email)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.Email).IsUnique().HasFilter("password_hash IS NOT NULL");
        });
        model.Entity<RecoveryCodeEntity>(e =>
        {
            e.ToTable("recovery_codes");
            e.HasKey(x => new { x.UserId, x.Hash });
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<IdentityEntity>(e =>
        {
            e.ToTable("identities");
            e.HasKey(x => new { x.Provider, x.Subject });
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SessionEntity>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<WorkspaceEntity>(e =>
        {
            e.ToTable("workspaces");
            e.HasKey(x => x.Id);
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OwnerId).IsUnique().HasFilter("personal");
            e.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        model.Entity<MemberEntity>(e =>
        {
            e.ToTable("members", t => t.HasCheckConstraint("members_normalized_email", "email = lower(email)"));
            e.HasKey(x => new { x.WorkspaceId, x.Email });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.InviteToken).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
        });
        model.Entity<FileEntity>(e =>
        {
            e.ToTable("files");
            e.HasKey(x => x.Id);
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UploaderId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ShareEntity>(e =>
        {
            e.ToTable("shares");
            e.HasKey(x => x.Token);
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.WorkspaceId, x.CardId }).IsUnique();
            e.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("shares_access", "access IN ('signed-in','members')"));
        });
        model.Entity<ProjectEntity>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<StatusEntity>(e =>
        {
            e.ToTable("statuses");
            e.HasKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<BucketEntity>(e =>
        {
            e.ToTable("buckets");
            e.HasKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<LabelEntity>(e =>
        {
            e.ToTable("labels");
            e.HasKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SwimlaneEntity>(e =>
        {
            e.ToTable("swimlanes");
            e.HasKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ProjectEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CardEntity>(e =>
        {
            e.ToTable("cards");
            e.HasKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ProjectEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StatusEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.StatusId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BucketEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.BucketId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SwimlaneEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.SwimlaneId }).HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.WorkspaceId, x.Due });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("cards_uppercase_id", "id ~ '^KB-[A-Z0-9-]+$'");
                t.HasCheckConstraint("cards_priority", "priority IN ('Low','Medium','High')");
            });
        });
        model.Entity<CardLabelEntity>(e =>
        {
            e.ToTable("card_labels");
            e.HasKey(x => new { x.WorkspaceId, x.CardId, x.LabelId });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<LabelEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.LabelId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CardAssigneeEntity>(e =>
        {
            e.ToTable("card_assignees");
            e.HasKey(x => new { x.WorkspaceId, x.CardId, x.MemberEmail });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MemberEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.MemberEmail }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CardAttachmentEntity>(e =>
        {
            e.ToTable("card_attachments");
            e.HasKey(x => new { x.WorkspaceId, x.CardId, x.FileId });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FileEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.FileId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CardCommentEntity>(e =>
        {
            e.ToTable("card_comments");
            e.HasKey(x => new { x.WorkspaceId, x.CardId, x.Position });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ChecklistItemEntity>(e =>
        {
            e.ToTable("checklist_items");
            e.HasKey(x => new { x.WorkspaceId, x.CardId, x.Position });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<HistoryEntryEntity>(e =>
        {
            e.ToTable("card_history");
            e.HasKey(x => new { x.WorkspaceId, x.CardId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<HistoryChangeEntity>(e =>
        {
            e.ToTable("history_changes");
            e.HasKey(x => new { x.WorkspaceId, x.CardId, x.HistoryId, x.Position });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CardEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<HistoryEntryEntity>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.CardId, x.HistoryId }).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ActivityEntity>(e =>
        {
            e.ToTable("workspace_activity");
            e.HasKey(x => new { x.WorkspaceId, x.Position });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<NotificationEntity>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => new { x.WorkspaceId, x.Id });
            e.HasOne<WorkspaceEntity>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
