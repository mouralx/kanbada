using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

/// <summary>Resolves the personal-workspace alias at the API boundary.</summary>
public sealed class WorkspaceResolver(KanbadaDbContext context)
{
    public async Task<Guid> WorkspaceId(string id, Guid user)
    {
        if (id != "studio" && Guid.TryParse(id, out var guid)) return guid;
        if (id != "studio") throw new ApiError(404, "Workspace not found.");
        return await context.Workspaces.Where(x => x.OwnerId == user && x.Personal).Select(x => (Guid?)x.Id).SingleOrDefaultAsync()
            ?? throw new ApiError(404, "Workspace not found.");
    }
}
