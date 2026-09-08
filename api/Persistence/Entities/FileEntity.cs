namespace Kanbada.Api;

public sealed class FileEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UploaderId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}
