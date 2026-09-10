namespace Kanbada.Api;

public sealed class SessionEntity
{
    public string Id { get; set; } = "";
    public Guid UserId { get; set; }
    public bool TwoFactorVerified { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
