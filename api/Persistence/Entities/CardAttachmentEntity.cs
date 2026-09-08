namespace Kanbada.Api;

public sealed class CardAttachmentEntity
{
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public Guid FileId { get; set; }
    public int Position { get; set; }
}
