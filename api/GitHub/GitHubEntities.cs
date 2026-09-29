namespace Kanbada.Api;

public sealed class GitHubConnectionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ProjectId { get; set; } = "";
    public string BaseUrl { get; set; } = "https://github.com/";
    public string Owner { get; set; } = "";
    public string OwnerType { get; set; } = "organization";
    public int ProjectNumber { get; set; }
    public string RemoteProjectId { get; set; } = "";
    public string Repository { get; set; } = "";
    public string RepositoryId { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public string Direction { get; set; } = "github-to-kanbada";
    public string StatusFieldId { get; set; } = "";
    public string PriorityFieldId { get; set; } = "";
    public string DueFieldId { get; set; } = "";
    public bool SyncAssignees { get; set; }
    public bool SyncLabels { get; set; } = true;
    public string Cron { get; set; } = "*/15 * * * *";
    public string TimeZone { get; set; } = "UTC";
    public bool Enabled { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastFinishedAt { get; set; }
    public string? LastError { get; set; }
    public int LastSyncedCount { get; set; }
    public List<GitHubMappingEntity> Mappings { get; set; } = [];
}

public sealed class GitHubMappingEntity
{
    public Guid ConnectionId { get; set; }
    public string Kind { get; set; } = "";
    public string KanbadaValue { get; set; } = "";
    public string GitHubValue { get; set; } = "";
    public bool IsDefault { get; set; } = true;
}

public sealed class GitHubLinkEntity
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string CardId { get; set; } = "";
    public string? ItemId { get; set; }
    public string? ContentId { get; set; }
    public string ContentType { get; set; } = "";
    public string? Url { get; set; }
    public string? DisplayKey { get; set; }
    public string Origin { get; set; } = "";
    public string? KanbadaHash { get; set; }
    public string? GitHubHash { get; set; }
    public bool CreationPending { get; set; }
    public bool Initialized { get; set; }
    public bool WritePending { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public Guid? SeenRunId { get; set; }
}

public sealed class GitHubApprovedHostEntity
{
    public string Authority { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
}

public sealed record GitHubMappingInput(string Kind, string KanbadaValue, string GitHubValue, bool IsDefault = true);
public sealed record GitHubConnectionInput(
    long Version, string BaseUrl, string Owner, string OwnerType, int ProjectNumber, string Repository,
    string? Token, string Direction, string StatusFieldId, string PriorityFieldId, string DueFieldId,
    bool SyncLabels, bool SyncAssignees, string Cron, string TimeZone, bool Enabled, List<GitHubMappingInput> Mappings);
public sealed record GitHubOption(string Id, string Name);
public sealed record GitHubField(string Id, string Name, string DataType, List<GitHubOption> Options);
public sealed record GitHubMetadata(string ProjectId, string Title, string Url, string RepositoryId, List<GitHubField> Fields);
public sealed record GitHubContent(string Id, string Type, string Title, string Body, string Url, string Key,
    string? RepositoryId, string[]? Labels, string[] Assignees);
public sealed record GitHubItem(string Id, bool Archived, GitHubContent? Content, Dictionary<string, string> Fields);
public sealed class GitHubSyncException(string message, DateTimeOffset? retryAt = null) : Exception(message)
{
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
