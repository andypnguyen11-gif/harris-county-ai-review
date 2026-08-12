using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Pass-through implementation of <see cref="ISearchQueryGateway"/> over the
/// Azure AI Search SDK client. The SDK's configured retry policy handles
/// transient failures; anything that survives it is translated so that a
/// search outage reads as "Search is unavailable" rather than leaking the
/// service endpoint through an SDK exception.
/// </summary>
public sealed class AzureSearchQueryGateway : ISearchQueryGateway
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<AzureSearchQueryGateway> _logger;

    public AzureSearchQueryGateway(
        SearchIndexClient indexClient,
        IOptions<SearchOptions> options,
        ILogger<AzureSearchQueryGateway>? logger = null)
    {
        _searchClient = indexClient.GetSearchClient(options.Value.IndexName);
        _logger = logger ?? NullLogger<AzureSearchQueryGateway>.Instance;
    }

    public Task<IReadOnlyList<ChunkSearchHit>> SearchAsync(
        ChunkSearchQuery query,
        CancellationToken cancellationToken)
        => AzureOperationExecutor.ExecuteAsync(
            ExternalServiceNames.Search,
            "query",
            token => SearchCoreAsync(query, token),
            cancellationToken,
            _logger);

    private async Task<IReadOnlyList<ChunkSearchHit>> SearchCoreAsync(
        ChunkSearchQuery query,
        CancellationToken cancellationToken)
    {
        var options = new global::Azure.Search.Documents.SearchOptions
        {
            Filter = query.Filter,
            Size = query.Size,
        };

        if (query.Vector is not null)
        {
            options.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(query.Vector)
                    {
                        KNearestNeighborsCount = query.Size,
                        Fields = { SearchIndexDefinition.Fields.Embedding },
                    },
                },
            };
        }

        if (query.UseSemanticRanking)
        {
            options.QueryType = SearchQueryType.Semantic;
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = query.SemanticConfigurationName,
            };
        }

        var hits = new List<ChunkSearchHit>();
        var response = await _searchClient.SearchAsync<SearchDocument>(
            query.SearchText, options, cancellationToken);
        await foreach (var result in response.Value.GetResultsAsync())
        {
            hits.Add(new ChunkSearchHit(
                result.Document, result.Score, result.SemanticSearch?.RerankerScore));

            if (hits.Count >= query.Size)
            {
                break;
            }
        }

        return hits;
    }
}
