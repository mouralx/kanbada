namespace Kanbada.Api;

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Photo { get; set; }
    public bool PhotoRequired { get; set; }
    public string? PasswordHash { get; set; }
    public string? TwoFactorSecret { get; set; }
    public string? TwoFactorPendingSecret { get; set; }
    public DateTimeOffset? TwoFactorPendingExpiresAt { get; set; }
    public long? TwoFactorLastStep { get; set; }
    public int TwoFactorFailedAttempts { get; set; }
    public DateTimeOffset? TwoFactorLockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
