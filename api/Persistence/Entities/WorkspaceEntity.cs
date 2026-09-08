namespace Kanbada.Api;

public sealed class WorkspaceEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public bool Personal { get; set; }
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    public string? Banner { get; set; }
    public double? BannerPosition { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
