using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Service registration for Azure AI Search indexing.
/// </summary>
public static class SearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDocumentIndexService"/> backed by Azure AI
    /// Search, along with <see cref="SearchOptions"/> bound from the
    /// <c>Search</c> configuration section.
    /// </summary>
    public static IServiceCollection AddSearchIndexing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SearchOptions>()
            .Bind(configuration.GetSection(SearchOptions.SectionName))
            .Validate(
                options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _),
                $"{SearchOptions.SectionName}:{nameof(SearchOptions.Endpoint)} must be an absolute URI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                $"{SearchOptions.SectionName}:{nameof(SearchOptions.ApiKey)} is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.IndexName),
                $"{SearchOptions.SectionName}:{nameof(SearchOptions.IndexName)} is required.")
            .ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<SearchOptions>>().Value;
            return new SearchIndexClient(
                new Uri(options.Endpoint),
                new AzureKeyCredential(options.ApiKey),
                new SearchClientOptions().WithResilience(provider.GetResilienceOptions()));
        });

        services.AddSingleton<ISearchIndexGateway, AzureSearchIndexGateway>();
        services.AddSingleton<IDocumentIndexService, AzureDocumentIndexService>();

        return services;
    }
}
