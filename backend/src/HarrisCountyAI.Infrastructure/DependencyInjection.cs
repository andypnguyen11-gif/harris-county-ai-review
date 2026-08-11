using HarrisCountyAI.Application.Cases;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;
using HarrisCountyAI.Infrastructure.Persistence;
using HarrisCountyAI.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddDocumentPersistence();
        services.AddValidationReports();
        services.AddBlobStorage(configuration);
        services.AddDocumentIntelligence(configuration);

        return services;
    }
}
