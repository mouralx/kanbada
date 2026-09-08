namespace Kanbada.Api;

public sealed class HistoryChangeEntity
{
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public string HistoryId { get; set; } = "";
    public int Position { get; set; }
    public string Text { get; set; } = "";
}
