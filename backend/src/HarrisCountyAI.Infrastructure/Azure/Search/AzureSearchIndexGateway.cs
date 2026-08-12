using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Pass-through implementation of <see cref="ISearchIndexGateway"/> over the
/// Azure AI Search SDK clients, with SDK failures translated into the
/// application's dependency vocabulary.
/// </summary>
/// <remarks>
/// Every operation here is idempotent — creating or updating an index,
/// uploading documents keyed by chunk id, deleting by chunk id — so the SDK
/// retrying a timed-out attempt cannot double-apply anything.
/// </remarks>
public sealed class AzureSearchIndexGateway : ISearchIndexGateway
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly ILogger<AzureSearchIndexGateway> _logger;

    public AzureSearchIndexGateway(
        SearchIndexClient indexClient,
        IOptions<SearchOptions> options,
        ILogger<AzureSearchIndexGateway>? logger = null)
    {
        _indexClient = indexClient;
        _searchClient = indexClient.GetSearchClient(options.Value.IndexName);
        _logger = logger ?? NullLogger<AzureSearchIndexGateway>.Instance;
    }

    public Task CreateOrUpdateIndexAsync(SearchIndex index, CancellationToken cancellationToken)
        => Execute(
            "create or update index",
            async token => await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: token),
            cancellationToken);

    public Task UploadDocumentsAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken)
        => Execute(
            "upload documents",
            async token => await _searchClient.UploadDocumentsAsync(documents, cancellationToken: token),
            cancellationToken);

    public Task DeleteDocumentsAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken)
        => Execute(
            "delete documents",
            async token => await _searchClient.DeleteDocumentsAsync(
                SearchIndexDefinition.Fields.ChunkId, chunkIds, cancellationToken: token),
            cancellationToken);

    public Task<IReadOnlyList<string>> FindChunkIdsAsync(string filter, CancellationToken cancellationToken)
        => AzureOperationExecutor.ExecuteAsync(
            ExternalServiceNames.Search,
            "find chunk ids",
            token => FindChunkIdsCoreAsync(filter, token),
            cancellationToken,
            _logger);

    private async Task<IReadOnlyList<string>> FindChunkIdsCoreAsync(string filter, CancellationToken cancellationToken)
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

    private Task Execute(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
        => AzureOperationExecutor.ExecuteAsync(
            ExternalServiceNames.Search, operation, action, cancellationToken, _logger);
}
