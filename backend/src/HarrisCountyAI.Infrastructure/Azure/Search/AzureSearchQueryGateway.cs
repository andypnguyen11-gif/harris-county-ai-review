using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Pass-through implementation of <see cref="ISearchQueryGateway"/> over the
/// Azure AI Search SDK client.
/// </summary>
public sealed class AzureSearchQueryGateway : ISearchQueryGateway
{
    private readonly SearchClient _searchClient;

    public AzureSearchQueryGateway(SearchIndexClient indexClient, IOptions<SearchOptions> options)
    {
        _searchClient = indexClient.GetSearchClient(options.Value.IndexName);
    }

    public async Task<IReadOnlyList<ChunkSearchHit>> SearchAsync(
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

        var hits = new List<ChunkSearchHit>();
        var response = await _searchClient.SearchAsync<SearchDocument>(
            query.SearchText, options, cancellationToken);
        await foreach (var result in response.Value.GetResultsAsync())
        {
            hits.Add(new ChunkSearchHit(result.Document, result.Score));

            if (hits.Count >= query.Size)
            {
                break;
            }
        }

        return hits;
    }
}
