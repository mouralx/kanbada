using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class FileEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api = api.MapGroup("").WithTags("Files");
        api.MapPost("/workspaces/{id}/files", async (string id, HttpContext ctx, WorkspaceResolver db, KanbadaDbContext context, WorkspaceStore store) =>
        {
            var user = Auth.User(ctx);
            var workspace = await db.WorkspaceId(id, user);
            await store.Read(workspace, user);
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? throw new ApiError(400, "Choose a file.");
            if (file.Length > 25 * 1024 * 1024)
                throw new ApiError(400, "Each file must be 25 MB or smaller.");
            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name))
                throw new ApiError(400, "A file name is required.");
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            var fileId = Guid.NewGuid();
            var at = DateTimeOffset.UtcNow;
            context.Files.Add(new FileEntity { Id = fileId, WorkspaceId = workspace, UploaderId = user, Name = name, ContentType = file.ContentType ?? "application/octet-stream", Bytes = stream.ToArray(), CreatedAt = at });
            await context.SaveChangesAsync();
            return Results.Ok(new { id = fileId, name, size = file.Length, type = file.ContentType, addedAt = at });
        }).DisableAntiforgery();
        api.MapGet("/files/{id:guid}", async (Guid id, HttpContext ctx, KanbadaDbContext context) =>
        {
            var user = Auth.User(ctx);
            var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && context.Members.Any(m => m.WorkspaceId == x.WorkspaceId && m.UserId == user))
                ?? throw new ApiError(404, "File not found or access denied.");
            return Results.File(file.Bytes, "application/octet-stream", file.Name);
        });
        api.MapDelete("/files/{id:guid}", async (Guid id, HttpContext ctx, KanbadaDbContext context) =>
        {
            var user = Auth.User(ctx);
            await context.Files.Where(x => x.Id == id && x.UploaderId == user && !context.CardAttachments.Any(a => a.FileId == x.Id)).ExecuteDeleteAsync();
            return Results.NoContent();
        });
    }
}
