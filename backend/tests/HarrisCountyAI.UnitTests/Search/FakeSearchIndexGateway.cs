using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using HarrisCountyAI.Infrastructure.Azure.Search;

namespace HarrisCountyAI.UnitTests.Search;

/// <summary>
/// In-memory <see cref="ISearchIndexGateway"/> that records every call for
/// assertion and serves canned chunk-id query results.
/// </summary>
public sealed class FakeSearchIndexGateway : ISearchIndexGateway
{
    public List<SearchIndex> CreatedOrUpdatedIndexes { get; } = [];

    public List<IReadOnlyList<SearchDocument>> UploadedBatches { get; } = [];

    public List<IReadOnlyList<string>> DeletedChunkIdBatches { get; } = [];

    public List<string> ExecutedFilters { get; } = [];

    /// <summary>Chunk ids returned by <see cref="FindChunkIdsAsync"/>.</summary>
    public IReadOnlyList<string> ChunkIdsToReturn { get; set; } = [];

    public Task CreateOrUpdateIndexAsync(SearchIndex index, CancellationToken cancellationToken)
    {
        CreatedOrUpdatedIndexes.Add(index);
        return Task.CompletedTask;
    }

    public Task UploadDocumentsAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken)
    {
        UploadedBatches.Add(documents);
        return Task.CompletedTask;
    }

    public Task DeleteDocumentsAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken)
    {
        DeletedChunkIdBatches.Add(chunkIds);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> FindChunkIdsAsync(string filter, CancellationToken cancellationToken)
    {
        ExecutedFilters.Add(filter);
        return Task.FromResult(ChunkIdsToReturn);
    }
}
