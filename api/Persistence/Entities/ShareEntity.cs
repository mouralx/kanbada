namespace Kanbada.Api;

public sealed class ShareEntity
{
    public string Token { get; set; } = "";
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public Guid CreatorId { get; set; }
    public string Access { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
