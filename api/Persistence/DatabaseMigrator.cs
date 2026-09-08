using Microsoft.EntityFrameworkCore;

namespace Kanbada.Api;

public sealed class DatabaseMigrator(KanbadaDbContext db)
{
    public Task Initialize(CancellationToken cancellationToken = default) => db.Database.MigrateAsync(cancellationToken);
}
