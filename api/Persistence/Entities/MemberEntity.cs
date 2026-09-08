namespace Kanbada.Api;

public sealed class MemberEntity
{
    public Guid WorkspaceId { get; set; }
    public string Email { get; set; } = "";
    public Guid? UserId { get; set; }
    public string? InviteToken { get; set; }
    public string Name { get; set; } = "";
    public string Initials { get; set; } = "";
    public string Color { get; set; } = "";
    public string? Photo { get; set; }
    public int Position { get; set; }
}
