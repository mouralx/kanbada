namespace Kanbada.Api;

public sealed class CardCommentEntity
{
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public int Position { get; set; }
    public string Text { get; set; } = "";
}
