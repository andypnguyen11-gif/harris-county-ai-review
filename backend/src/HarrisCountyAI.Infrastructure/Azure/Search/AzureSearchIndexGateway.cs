using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Pass-through implementation of <see cref="ISearchIndexGateway"/> over the
/// Azure AI Search SDK clients.
/// </summary>
public sealed class AzureSearchIndexGateway : ISearchIndexGateway
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;

    public AzureSearchIndexGateway(SearchIndexClient indexClient, IOptions<SearchOptions> options)
    {
        _indexClient = indexClient;
        _searchClient = indexClient.GetSearchClient(options.Value.IndexName);
    }

    public async Task CreateOrUpdateIndexAsync(SearchIndex index, CancellationToken cancellationToken)
        => await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);

    public async Task UploadDocumentsAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken)
        => await _searchClient.UploadDocumentsAsync(documents, cancellationToken: cancellationToken);

    public async Task DeleteDocumentsAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken)
        => await _searchClient.DeleteDocumentsAsync(
            SearchIndexDefinition.Fields.ChunkId, chunkIds, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<string>> FindChunkIdsAsync(string filter, CancellationToken cancellationToken)
    {
        var options = new global::Azure.Search.Documents.SearchOptions
        {
            Filter = filter,
            Size = 1000,
            Select = { SearchIndexDefinition.Fields.ChunkId },
        };

        var chunkIds = new List<string>();
        var response = await _searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken);
        await foreach (var result in response.Value.GetResultsAsync())
        {
            if (result.Document.TryGetValue(SearchIndexDefinition.Fields.ChunkId, out var value)
                && value is string chunkId)
            {
                chunkIds.Add(chunkId);
            }
        }

        return chunkIds;
    }
}
