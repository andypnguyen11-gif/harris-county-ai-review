using HarrisCountyAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.IntegrationTests.Persistence;

/// <summary>
/// Creates a uniquely named database on the local SQL Server, applies migrations,
/// and drops the database when the fixture is disposed.
/// </summary>
public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    public string DatabaseName { get; } = $"HarrisCountyAI_Test_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Server=localhost,1433;Database={DatabaseName};User Id=sa;Password=LocalDev!Passw0rd;TrustServerCertificate=True";

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
