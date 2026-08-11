using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;

namespace HarrisCountyAI.UnitTests.Search.Retrieval;

public class AzureRetrievalServiceTests
{
    private readonly FakeSearchQueryGateway _gateway = new();
    private readonly FakeEmbeddingService _embeddingService = new();
    private readonly AzureRetrievalService _service;

    public AzureRetrievalServiceTests()
    {
        _service = new AzureRetrievalService(_gateway, _embeddingService);
    }

    private static RetrievalRequest Request(
        string query = "What documents must a floodplain permit include?",
        int topK = RetrievalRequest.DefaultTopK,
        string? department = null,
        string? permitType = null,
        string? documentType = null) => new()
        {
            Query = query,
            TopK = topK,
            Department = department,
            PermitType = permitType,
            DocumentType = documentType,
        };

    private static SearchDocument ChunkDocument(
        string chunkId = "0f8fad5bd9cb469fa165408319b0e0d9-0000",
        Guid? documentId = null,
        string text = "A completed application form is required.",
        string title = "Floodplain Regulations")
    {
        var document = new SearchDocument
        {
            [SearchIndexDefinition.Fields.ChunkId] = chunkId,
            [SearchIndexDefinition.Fields.DocumentId] = (documentId ?? Guid.NewGuid()).ToString("D"),
            [SearchIndexDefinition.Fields.Text] = text,
            [SearchIndexDefinition.Fields.Title] = title,
        };
        return document;
    }

    [Fact]
    public async Task Always_Filters_To_The_KnowledgeBase_Corpus()
    {
        await _service.RetrieveAsync(Request());

        Assert.Equal("sourceType eq 'KnowledgeBase'", _gateway.LastQuery!.Filter);
    }

    [Fact]
    public async Task Adds_Metadata_Filters_When_Provided()
    {
        await _service.RetrieveAsync(Request(
            department: "Engineering",
            permitType: "FloodplainDevelopmentPermit",
            documentType: "Regulation"));

        Assert.Equal(
            "sourceType eq 'KnowledgeBase'"
            + " and department eq 'Engineering'"
            + " and permitType eq 'FloodplainDevelopmentPermit'"
            + " and documentType eq 'Regulation'",
            _gateway.LastQuery!.Filter);
    }

    [Fact]
    public async Task Escapes_Single_Quotes_In_Filter_Values()
    {
        await _service.RetrieveAsync(Request(department: "Harris County's Engineering"));

        Assert.Contains("department eq 'Harris County''s Engineering'", _gateway.LastQuery!.Filter);
    }

    [Fact]
    public async Task Embeds_The_Query_Text_And_Passes_The_Vector_To_The_Gateway()
    {
        _embeddingService.VectorToReturn = [0.5f, 0.25f];

        await _service.RetrieveAsync(Request(query: "setback requirements"));

        var batch = Assert.Single(_embeddingService.ReceivedBatches);
        Assert.Equal(["setback requirements"], batch);
        Assert.NotNull(_gateway.LastQuery!.Vector);
        Assert.Equal([0.5f, 0.25f], _gateway.LastQuery.Vector);
    }

    [Fact]
    public async Task Passes_TopK_As_The_Query_Size()
    {
        await _service.RetrieveAsync(Request(topK: 12));

        Assert.Equal(12, _gateway.LastQuery!.Size);
    }

    [Fact]
    public async Task Maps_Hits_To_Retrieved_Chunks_With_Metadata_And_Score()
    {
        var documentId = Guid.NewGuid();
        var effectiveDate = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var document = ChunkDocument(documentId: documentId);
        document[SearchIndexDefinition.Fields.Section] = "Section 4.2";
        document[SearchIndexDefinition.Fields.Page] = 17;
        document[SearchIndexDefinition.Fields.Department] = "Engineering";
        document[SearchIndexDefinition.Fields.PermitType] = "FloodplainDevelopmentPermit";
        document[SearchIndexDefinition.Fields.DocumentType] = "Regulation";
        document[SearchIndexDefinition.Fields.EffectiveDate] = effectiveDate;
        document[SearchIndexDefinition.Fields.SourceUrl] = "https://www.hcfcd.org/regulations";
        _gateway.HitsToReturn = [new ChunkSearchHit(document, 0.87)];

        var chunks = await _service.RetrieveAsync(Request());

        var chunk = Assert.Single(chunks);
        Assert.Equal("0f8fad5bd9cb469fa165408319b0e0d9-0000", chunk.ChunkId);
        Assert.Equal(documentId, chunk.DocumentId);
        Assert.Equal("A completed application form is required.", chunk.Text);
        Assert.Equal("Floodplain Regulations", chunk.Title);
        Assert.Equal("Section 4.2", chunk.Section);
        Assert.Equal(17, chunk.Page);
        Assert.Equal("Engineering", chunk.Department);
        Assert.Equal("FloodplainDevelopmentPermit", chunk.PermitType);
        Assert.Equal("Regulation", chunk.DocumentType);
        Assert.Equal(effectiveDate, chunk.EffectiveDate);
        Assert.Equal("https://www.hcfcd.org/regulations", chunk.SourceUrl);
        Assert.Equal(0.87, chunk.Score);
    }

    [Fact]
    public async Task Maps_Missing_Optional_Fields_To_Null()
    {
        _gateway.HitsToReturn = [new ChunkSearchHit(ChunkDocument(), 1.0)];

        var chunk = Assert.Single(await _service.RetrieveAsync(Request()));

        Assert.Null(chunk.Section);
        Assert.Null(chunk.Page);
        Assert.Null(chunk.Department);
        Assert.Null(chunk.PermitType);
        Assert.Null(chunk.DocumentType);
        Assert.Null(chunk.EffectiveDate);
        Assert.Null(chunk.SourceUrl);
    }

    [Fact]
    public async Task Skips_Hits_Missing_Required_Fields()
    {
        var broken = new SearchDocument
        {
            [SearchIndexDefinition.Fields.ChunkId] = "broken-0000",
            // No documentId, text, or title.
        };
        _gateway.HitsToReturn =
        [
            new ChunkSearchHit(broken, 0.9),
            new ChunkSearchHit(ChunkDocument(), 0.8),
        ];

        var chunks = await _service.RetrieveAsync(Request());

        var chunk = Assert.Single(chunks);
        Assert.Equal("0f8fad5bd9cb469fa165408319b0e0d9-0000", chunk.ChunkId);
    }

    [Fact]
    public async Task Returns_Empty_When_The_Index_Has_No_Matches()
    {
        var chunks = await _service.RetrieveAsync(Request());

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task Missing_Score_Maps_To_Zero()
    {
        _gateway.HitsToReturn = [new ChunkSearchHit(ChunkDocument(), null)];

        var chunk = Assert.Single(await _service.RetrieveAsync(Request()));

        Assert.Equal(0d, chunk.Score);
    }

    [Fact]
    public async Task Rejects_A_Null_Request()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RetrieveAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_An_Empty_Query(string query)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RetrieveAsync(Request(query: query)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RetrievalRequest.MaxTopK + 1)]
    public async Task Rejects_TopK_Out_Of_Range(int topK)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _service.RetrieveAsync(Request(topK: topK)));

        Assert.Empty(_gateway.ExecutedQueries);
    }

    [Fact]
    public async Task Throws_When_The_Embedding_Service_Returns_Nothing()
    {
        _embeddingService.ReturnEmpty = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RetrieveAsync(Request()));

        Assert.Empty(_gateway.ExecutedQueries);
    }
}
