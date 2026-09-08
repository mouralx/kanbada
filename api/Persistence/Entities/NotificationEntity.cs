namespace Kanbada.Api;

public sealed class NotificationEntity
{
    public Guid WorkspaceId { get; set; }
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public string At { get; set; } = "";
    public int Position { get; set; }
}
