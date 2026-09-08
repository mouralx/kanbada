namespace Kanbada.Api;

public sealed class ProjectEntity
{
    public Guid WorkspaceId { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Archived { get; set; }
    public string? System { get; set; }
    public int Position { get; set; }
}
