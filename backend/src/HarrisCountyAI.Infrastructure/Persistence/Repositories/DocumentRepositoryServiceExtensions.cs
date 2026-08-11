using HarrisCountyAI.Application.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Service registration for document persistence. Call from the composition
/// root alongside the other infrastructure registrations:
/// <c>services.AddDocumentPersistence();</c>.
/// </summary>
public static class DocumentRepositoryServiceExtensions
{
    public static IServiceCollection AddDocumentPersistence(this IServiceCollection services)
    {
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        return services;
    }
}
