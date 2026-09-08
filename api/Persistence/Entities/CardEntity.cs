namespace Kanbada.Api;

public sealed class CardEntity
{
    public Guid WorkspaceId { get; set; }
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string StatusId { get; set; } = "";
    public string? BucketId { get; set; }
    public string? SwimlaneId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateOnly? Due { get; set; }
    public string? Cover { get; set; }
    public int Position { get; set; }
}
