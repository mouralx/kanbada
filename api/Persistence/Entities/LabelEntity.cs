namespace Kanbada.Api;

public sealed class LabelEntity
{
    public Guid WorkspaceId { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public bool Complete { get; set; }
    public int Position { get; set; }
}
