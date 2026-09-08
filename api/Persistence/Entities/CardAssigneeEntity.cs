namespace Kanbada.Api;

public sealed class CardAssigneeEntity
{
    public Guid WorkspaceId { get; set; }
    public string CardId { get; set; } = "";
    public string MemberEmail { get; set; } = "";
    public int Position { get; set; }
}
