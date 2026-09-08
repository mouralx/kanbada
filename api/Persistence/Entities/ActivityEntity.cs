namespace Kanbada.Api;

public sealed class ActivityEntity
{
    public Guid WorkspaceId { get; set; }
    public int Position { get; set; }
    public string Text { get; set; } = "";
}
