namespace Kanbada.Api;

public sealed class JiraConnectionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ProjectId { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Edition { get; set; } = "data-center";
    public string Email { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public string Jql { get; set; } = "";
    public string JiraProjectKey { get; set; } = "";
    public string IssueTypeId { get; set; } = "";
    public string Direction { get; set; } = "jira-to-kanbada";
    public string Cron { get; set; } = "*/15 * * * *";
    public string TimeZone { get; set; } = "UTC";
    public bool Enabled { get; set; }
    public bool SyncAssignees { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastFinishedAt { get; set; }
    public string? LastError { get; set; }
    public int LastSyncedCount { get; set; }
    public List<JiraMappingEntity> Mappings { get; set; } = [];
}

public sealed class JiraMappingEntity
{
    public Guid ConnectionId { get; set; }
    public string Kind { get; set; } = "";
    public string KanbadaValue { get; set; } = "";
    public string JiraValue { get; set; } = "";
    public bool IsDefault { get; set; } = true;
}

public sealed class JiraLinkEntity
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string CardId { get; set; } = "";
    public string? JiraIssueId { get; set; }
    public string? JiraKey { get; set; }
    public string Origin { get; set; } = "";
    public string? KanbadaHash { get; set; }
    public string? JiraHash { get; set; }
    public bool CreationPending { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
