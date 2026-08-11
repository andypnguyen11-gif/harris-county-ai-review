using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.LanguageModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Service registration for corpus retrieval over Azure AI Search.
/// </summary>
public static class RetrievalServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRetrievalService"/> backed by Azure AI Search,
    /// along with the embedding service it uses for query vectors. Uses
    /// try-add semantics throughout, so it composes safely with other callers
    /// of <c>AddEmbeddingService</c>. Requires <c>AddSearchIndexing</c> for
    /// the shared <c>SearchIndexClient</c> and <c>SearchOptions</c>
    /// registrations.
    /// </summary>
    public static IServiceCollection AddCorpusRetrieval(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddEmbeddingService(configuration);

        services.AddOptions<RetrievalOptions>()
            .Bind(configuration.GetSection(RetrievalOptions.SectionName))
            .Validate(
                options => options.DefaultTopK is >= 1 and <= RetrievalRequest.MaxTopK,
                $"{RetrievalOptions.SectionName}:{nameof(RetrievalOptions.DefaultTopK)} must be between 1 and {RetrievalRequest.MaxTopK}.")
            .Validate(
                options => Enum.IsDefined(options.Mode),
                $"{RetrievalOptions.SectionName}:{nameof(RetrievalOptions.Mode)} must be one of: {string.Join(", ", Enum.GetNames<RetrievalMode>())}.")
            .ValidateOnStart();

        services.TryAddSingleton<ISearchQueryGateway, AzureSearchQueryGateway>();
        services.TryAddSingleton<IRetrievalService, AzureRetrievalService>();

        return services;
    }
}
