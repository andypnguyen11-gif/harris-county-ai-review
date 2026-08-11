using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Application.Documents.Indexing;

public static class CaseDocumentIndexingServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ICaseDocumentIndexingService"/>. Requires
    /// <c>IDocumentChunkingService</c> (registered by <c>AddApplication</c>),
    /// <c>IEmbeddingService</c> (<c>AddEmbeddingService</c>) and
    /// <c>IDocumentIndexService</c> (<c>AddSearchIndexing</c>) from
    /// Infrastructure. Try-add semantics, so tests and the composition root
    /// can both call it safely.
    /// </summary>
    public static IServiceCollection AddCaseDocumentIndexing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ICaseDocumentIndexingService, CaseDocumentIndexingService>();
        return services;
    }
}
