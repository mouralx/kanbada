using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kanbada.Api;

/// <summary>Allows EF tooling to generate and apply migrations without starting the web host.</summary>
public sealed class KanbadaDbContextFactory : IDesignTimeDbContextFactory<KanbadaDbContext>
{
    public KanbadaDbContext CreateDbContext(string[] args)
    {
        var directory = File.Exists("Kanbada.Api.csproj") ? Directory.GetCurrentDirectory() : Path.Combine(Directory.GetCurrentDirectory(), "api");
        var configuration = new ConfigurationBuilder().SetBasePath(directory).AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Database=kanbada;Username=kanbada";
        return new KanbadaDbContext(new DbContextOptionsBuilder<KanbadaDbContext>().UseNpgsql(connection).Options);
    }
}
