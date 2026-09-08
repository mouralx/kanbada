namespace Kanbada.Api;

public sealed class HistoryEntryEntity
{
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public string Id { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public int Position { get; set; }
}
