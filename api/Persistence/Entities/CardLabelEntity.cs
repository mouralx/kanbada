namespace Kanbada.Api;

public sealed class CardLabelEntity
{
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public string LabelId { get; set; } = "";
    public int Position { get; set; }
}
