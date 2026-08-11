using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Service registration for semantic reranking over Azure AI Search.
/// </summary>
public static class RerankingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRerankingService"/> backed by Azure AI Search
    /// semantic ranking, with <see cref="RerankingOptions"/> bound from the
    /// <c>Reranking</c> configuration section. Uses try-add semantics, so it
    /// composes safely with repeated calls and with <c>AddCorpusRetrieval</c>
    /// (which calls this and try-adds the same query gateway). Reranking
    /// defaults to disabled; every option has a safe default. Requires
    /// <c>AddSearchIndexing</c> for the shared <c>SearchIndexClient</c> and
    /// <c>SearchOptions</c> registrations.
    /// </summary>
    public static IServiceCollection AddSemanticReranking(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RerankingOptions>()
            .Bind(configuration.GetSection(RerankingOptions.SectionName))
            .Validate(
                options => options.CandidatePoolSize is >= 1 and <= RetrievalRequest.MaxTopK,
                $"{RerankingOptions.SectionName}:{nameof(RerankingOptions.CandidatePoolSize)} must be between 1 and {RetrievalRequest.MaxTopK}.")
            .Validate(
                options => !options.Enabled || !string.IsNullOrWhiteSpace(options.SemanticConfigurationName),
                $"{RerankingOptions.SectionName}:{nameof(RerankingOptions.SemanticConfigurationName)} is required when reranking is enabled.")
            .ValidateOnStart();

        services.TryAddSingleton<ISearchQueryGateway, AzureSearchQueryGateway>();
        services.TryAddSingleton<IRerankingService, AzureSemanticRerankingService>();

        return services;
    }
}
