namespace Kanbada.Api;

public sealed class ExportJobEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Query { get; set; } = "";
    public string Locale { get; set; } = "en-US";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "queued";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int Processed { get; set; }
    public int? Total { get; set; }
    public int Attempts { get; set; }
    public long Bytes { get; set; }
    public long? SnapshotVersion { get; set; }
    public string? Error { get; set; }
}

public sealed class ExportChunkEntity
{
    public Guid ExportId { get; set; }
    public int Position { get; set; }
    public byte[] Bytes { get; set; } = [];
}

public sealed record ExportRequest(Guid Id, string Kind, CardQuery? Query = null, string Locale = "en-US");
