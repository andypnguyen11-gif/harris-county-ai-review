using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HarrisCountyAI.Infrastructure.Persistence;

/// <summary>Creates the DbContext for design-time tooling (dotnet ef migrations).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=HarrisCountyAI;User Id=sa;Password=LocalDev!Passw0rd;TrustServerCertificate=True")
            .Options;

        return new ApplicationDbContext(options);
    }
}
