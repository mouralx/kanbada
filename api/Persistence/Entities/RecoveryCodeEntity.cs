namespace Kanbada.Api;

public sealed class RecoveryCodeEntity
{
    public Guid UserId { get; set; }
    public string Hash { get; set; } = "";
}
