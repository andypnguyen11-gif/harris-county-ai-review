using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Search;

public class AzureDocumentIndexServiceTests
{
    private readonly FakeSearchIndexGateway _gateway = new();
    private readonly AzureDocumentIndexService _service;

    public AzureDocumentIndexServiceTests()
    {
        _service = new AzureDocumentIndexService(
            _gateway,
            Options.Create(new SearchOptions { IndexName = "unit-test-index" }));
    }

    private static IndexableChunk SampleChunk(
        Guid? documentId = null,
        string sourceType = IndexSourceTypes.KnowledgeBase,
        Guid? caseId = null,
        int embeddingLength = 1536)
    {
        var id = documentId ?? Guid.NewGuid();
        return new IndexableChunk
        {
            ChunkId = $"{id:N}-0000",
            DocumentId = id,
            Sequence = 0,
            Text = "Elevation certificates must be sealed by a licensed surveyor.",
            Section = "2.1 Elevation Requirements",
            PageNumber = 4,
            SourceType = sourceType,
            Title = "Floodplain Management Regulations",
            Department = "Engineering",
            PermitType = "Floodplain",
            DocumentType = "Regulation",
            EffectiveDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SourceUrl = "https://www.harriscountyfemt.org/regulations.pdf",
            CaseId = caseId,
            Embedding = new float[embeddingLength],
        };
    }

    [Fact]
    public async Task EnsureIndexAsync_Creates_Or_Updates_The_Index_With_The_Configured_Name()
    {
        await _service.EnsureIndexAsync();

        var index = Assert.Single(_gateway.CreatedOrUpdatedIndexes);
        Assert.Equal("unit-test-index", index.Name);
        Assert.Equal(14, index.Fields.Count);
    }

    [Fact]
    public async Task IndexAsync_Maps_Chunk_Fields_Onto_The_Index_Schema()
    {
        var documentId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var chunk = SampleChunk(documentId, IndexSourceTypes.CaseDocument, caseId);

        await _service.IndexAsync([chunk]);

        var batch = Assert.Single(_gateway.UploadedBatches);
        var document = Assert.Single(batch);
        Assert.Equal(chunk.ChunkId, document["chunkId"]);
        Assert.Equal(documentId.ToString("D"), document["documentId"]);
        Assert.Equal("CaseDocument", document["sourceType"]);
        Assert.Equal("Floodplain Management Regulations", document["title"]);
        Assert.Equal("Engineering", document["department"]);
        Assert.Equal("Floodplain", document["permitType"]);
        Assert.Equal("Regulation", document["documentType"]);
        Assert.Equal("2.1 Elevation Requirements", document["section"]);
        Assert.Equal(4, document["page"]);
        Assert.Equal(chunk.EffectiveDate, document["effectiveDate"]);
        Assert.Equal(chunk.SourceUrl, document["sourceUrl"]);
        Assert.Equal(chunk.Text, document["text"]);
        Assert.Equal(chunk.Embedding, document["embedding"]);
        Assert.Equal(caseId.ToString("D"), document["caseId"]);
    }

    [Fact]
    public async Task IndexAsync_Leaves_CaseId_Null_For_KnowledgeBase_Chunks()
    {
        await _service.IndexAsync([SampleChunk()]);

        var document = Assert.Single(Assert.Single(_gateway.UploadedBatches));
        Assert.Equal("KnowledgeBase", document["sourceType"]);
        Assert.Null(document["caseId"]);
    }

    [Fact]
    public async Task IndexAsync_Uploads_All_Chunks_In_One_Batch()
    {
        var documentId = Guid.NewGuid();
        var chunks = Enumerable.Range(0, 3)
            .Select(i => SampleChunk(documentId) with { ChunkId = $"{documentId:N}-{i:D4}", Sequence = i })
            .ToList();

        await _service.IndexAsync(chunks);

        var batch = Assert.Single(_gateway.UploadedBatches);
        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public async Task IndexAsync_Is_A_NoOp_For_An_Empty_List()
    {
        await _service.IndexAsync([]);

        Assert.Empty(_gateway.UploadedBatches);
    }

    [Fact]
    public async Task IndexAsync_Rejects_An_Unknown_SourceType()
    {
        var chunk = SampleChunk(sourceType: "SomethingElse");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _service.IndexAsync([chunk]));

        Assert.Contains("SomethingElse", exception.Message);
        Assert.Empty(_gateway.UploadedBatches);
    }

    [Fact]
    public async Task IndexAsync_Rejects_An_Embedding_With_Wrong_Dimensions()
    {
        var chunk = SampleChunk(embeddingLength: 768);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _service.IndexAsync([chunk]));

        Assert.Contains("1536", exception.Message);
        Assert.Empty(_gateway.UploadedBatches);
    }

    [Fact]
    public async Task DeleteDocumentAsync_Deletes_Every_Chunk_The_Document_Owns()
    {
        var documentId = Guid.NewGuid();
        _gateway.ChunkIdsToReturn = [$"{documentId:N}-0000", $"{documentId:N}-0001"];

        await _service.DeleteDocumentAsync(documentId);

        var filter = Assert.Single(_gateway.ExecutedFilters);
        Assert.Equal($"documentId eq '{documentId:D}'", filter);
        var deleted = Assert.Single(_gateway.DeletedChunkIdBatches);
        Assert.Equal(_gateway.ChunkIdsToReturn, deleted);
    }

    [Fact]
    public async Task DeleteDocumentAsync_Skips_Deletion_When_No_Chunks_Match()
    {
        _gateway.ChunkIdsToReturn = [];

        await _service.DeleteDocumentAsync(Guid.NewGuid());

        Assert.Single(_gateway.ExecutedFilters);
        Assert.Empty(_gateway.DeletedChunkIdBatches);
    }
}
