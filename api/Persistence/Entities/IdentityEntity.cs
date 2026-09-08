namespace Kanbada.Api;

public sealed class IdentityEntity
{
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid UserId { get; set; }
}
