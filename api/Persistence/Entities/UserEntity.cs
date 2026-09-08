namespace Kanbada.Api;

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string? PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
