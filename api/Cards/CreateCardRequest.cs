namespace Kanbada.Api;

/// <summary>Card creation command. Identity, audit history, and workspace version are server-owned.</summary>
public sealed record CreateCardRequest(
    string Title,
    string Project = "my-activities",
    string? Status = null,
    string Priority = "Medium",
    string Description = "",
    string Due = "",
    string Bucket = "",
    string Swimlane = "",
    string[]? Labels = null,
    string[]? Assignees = null,
    string[]? Comments = null,
    ChecklistInput[]? Checklist = null,
    AttachmentInput[]? Attachments = null);

public sealed record ChecklistInput(string Text, bool Done = false);
public sealed record AttachmentInput(Guid Id, string Name, long Size, string Type, DateTimeOffset AddedAt);
