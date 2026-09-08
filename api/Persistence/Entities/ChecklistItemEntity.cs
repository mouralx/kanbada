namespace Kanbada.Api;

public sealed class ChecklistItemEntity
{
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public int Position { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}
