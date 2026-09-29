using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public static class ExportEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var exports = api.MapGroup("/workspaces/{id}").WithTags("Exports");
        exports.MapPost("/exports", async (string id, ExportRequest request, WorkspaceResolver resolver, ExportJobs jobs, TimeProvider clock, HttpContext ctx, CancellationToken ct) =>
        {
            var user = Auth.User(ctx);
            var job = await jobs.Create(await resolver.WorkspaceId(id, user), user, request, ct);
            ctx.Response.Headers.CacheControl = "private, no-store";
            return Results.Accepted($"/api/workspaces/{Uri.EscapeDataString(id)}/exports/{job.Id}", ExportJobs.Summary(job, clock.GetUtcNow()));
        });
        exports.MapGet("/exports", async (string id, string? after, WorkspaceResolver resolver, ExportJobs jobs, HttpContext ctx, CancellationToken ct) =>
        {
            var user = Auth.User(ctx);
            ctx.Response.Headers.CacheControl = "private, no-store";
            return await jobs.List(await resolver.WorkspaceId(id, user), user, after, ct);
        });
        exports.MapGet("/exports/{exportId:guid}", async (string id, Guid exportId, WorkspaceResolver resolver, ExportJobs jobs, TimeProvider clock, HttpContext ctx, CancellationToken ct) =>
        {
            var user = Auth.User(ctx);
            ctx.Response.Headers.CacheControl = "private, no-store";
            return ExportJobs.Summary(await jobs.Find(await resolver.WorkspaceId(id, user), user, exportId, ct), clock.GetUtcNow());
        });
        exports.MapGet("/exports/{exportId:guid}/download", async (string id, Guid exportId, WorkspaceResolver resolver, ExportJobs jobs, KanbadaDbContext db, TimeProvider clock, HttpContext ctx, CancellationToken ct) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var user = Auth.User(ctx);
            var job = await jobs.Find(await resolver.WorkspaceId(id, user), user, exportId, ct);
            if (job.Kind is not ("project-xlsx" or "workspace-xlsx" or "dashboard-pdf"))
                throw new ApiError(410, "This export format is no longer available. Request a new export.");
            if (job.ExpiresAt <= clock.GetUtcNow() || job.Status == "expired") throw new ApiError(410, "This export has expired. Request a new export.");
            if (job.Status != "completed") throw new ApiError(409, "This export is not ready to download.");
            ctx.Response.Headers.CacheControl = "private, no-store";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.ContentLength = job.Bytes;
            var pdf = job.Kind == "dashboard-pdf";
            await Results.Stream(async stream =>
            {
                await foreach (var bytes in db.ExportChunks.AsNoTracking().Where(c => c.ExportId == exportId).OrderBy(c => c.Position).Select(c => c.Bytes).AsAsyncEnumerable().WithCancellation(ct))
                    await stream.WriteAsync(bytes, ct);
            }, pdf ? "application/pdf" : ExportWorkbook.ContentType, $"kanbada-{job.Id}.{(pdf ? "pdf" : "xlsx")}").ExecuteAsync(ctx);
            await snapshot.CommitAsync(ct);
        });
    }
}
