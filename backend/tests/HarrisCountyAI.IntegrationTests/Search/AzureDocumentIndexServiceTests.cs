using Azure;
using Azure.Search.Documents.Indexes;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Options;
using SearchOptions = HarrisCountyAI.Infrastructure.Azure.Search.SearchOptions;

namespace HarrisCountyAI.IntegrationTests.Search;

/// <summary>
/// End-to-end tests against a real Azure AI Search service. Deploys the index
/// schema with <c>EnsureIndexAsync</c>, then round-trips a sample chunk
/// (index, find, delete) under a throwaway document id so the shared index is
/// left unchanged. Skips when no search credentials are configured.
/// </summary>
public sealed class AzureDocumentIndexServiceTests
{
    private readonly SearchIndexClient _indexClient;
    private readonly AzureSearchIndexGateway _gateway;
    private readonly AzureDocumentIndexService _service;
    private readonly string _indexName;

    public AzureDocumentIndexServiceTests()
    {
        // When credentials are absent every [AzureSearchFact] is skipped, so
        // the placeholder fallbacks below are never actually used to connect.
        var options = new SearchOptions
        {
            Endpoint = AzureSearchEnvironment.Endpoint ?? "https://unconfigured.search.windows.net",
            ApiKey = AzureSearchEnvironment.ApiKey ?? "unconfigured",
            IndexName = AzureSearchEnvironment.IndexName,
        };
        _indexName = options.IndexName;
        _indexClient = new SearchIndexClient(
            new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));
        _gateway = new AzureSearchIndexGateway(_indexClient, Options.Create(options));
        _service = new AzureDocumentIndexService(_gateway, Options.Create(options));
    }

    [AzureSearchFact]
    public async Task EnsureIndexAsync_Deploys_The_Schema_To_The_Real_Service()
    {
        await _service.EnsureIndexAsync();

        var index = (await _indexClient.GetIndexAsync(_indexName)).Value;
        Assert.Equal(_indexName, index.Name);
        Assert.Equal(14, index.Fields.Count);
        var embedding = index.Fields.Single(f => f.Name == SearchIndexDefinition.Fields.Embedding);
        Assert.Equal(SearchIndexDefinition.EmbeddingDimensions, embedding.VectorSearchDimensions);
        Assert.Equal(SearchIndexDefinition.VectorProfileName, embedding.VectorSearchProfileName);
        Assert.NotNull(index.VectorSearch);
        Assert.Single(index.VectorSearch.Profiles);
    }

    [AzureSearchFact]
    public async Task Index_Then_Delete_Round_Trips_A_Sample_Chunk()
    {
        await _service.EnsureIndexAsync();

        var documentId = Guid.NewGuid();
        var embedding = new float[SearchIndexDefinition.EmbeddingDimensions];
        embedding[0] = 1f;
        var chunk = new IndexableChunk
        {
            ChunkId = $"{documentId:N}-0000",
            DocumentId = documentId,
            Sequence = 0,
            Text = "Integration test chunk. Safe to delete.",
            Section = "Test Section",
            PageNumber = 1,
            SourceType = IndexSourceTypes.KnowledgeBase,
            Title = "Integration Test Document",
            Department = "Engineering",
            PermitType = "Floodplain",
            DocumentType = "Test",
            EffectiveDate = DateTimeOffset.UtcNow,
            SourceUrl = "https://example.org/integration-test",
            CaseId = null,
            Embedding = embedding,
        };

        try
        {
            await _service.IndexAsync([chunk]);

            var indexed = await WaitForChunkCountAsync(documentId, expected: 1);
            Assert.Equal(1, indexed);
        }
        finally
        {
            await _service.DeleteDocumentAsync(documentId);
        }

        var remaining = await WaitForChunkCountAsync(documentId, expected: 0);
        Assert.Equal(0, remaining);
    }

    /// <summary>
    /// Indexing is near-real-time, not immediate; polls until the number of
    /// chunks for <paramref name="documentId"/> reaches
    /// <paramref name="expected"/> or ~30 seconds elapse.
    /// </summary>
    private async Task<int> WaitForChunkCountAsync(Guid documentId, int expected)
    {
        var filter = $"{SearchIndexDefinition.Fields.DocumentId} eq '{documentId:D}'";
        var count = -1;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            count = (await _gateway.FindChunkIdsAsync(filter, CancellationToken.None)).Count;
            if (count == expected)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return count;
    }
}
