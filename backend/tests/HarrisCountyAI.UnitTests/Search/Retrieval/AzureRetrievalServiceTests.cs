using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;
using HarrisCountyAI.UnitTests.Search.Reranking;
using Microsoft.Extensions.Options;

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

    private AzureRetrievalService ServiceWith(RetrievalOptions options)
        => new(_gateway, _embeddingService, Options.Create(options));

    private static RetrievalRequest Request(
        string query = "What documents must a floodplain permit include?",
        int? topK = RetrievalRequest.DefaultTopK,
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

    [Theory]
    [InlineData("What does Section 4.2 of the floodplain regulations require?")]
    [InlineData("Where do I submit Form MT-EZ?")]
    [InlineData("Can I build a storage shed in the floodplain?")]
    public async Task Hybrid_Queries_Carry_Both_The_Search_Text_And_The_Vector(string query)
    {
        await _service.RetrieveAsync(Request(query: query));

        Assert.Equal(query, _gateway.LastQuery!.SearchText);
        Assert.NotNull(_gateway.LastQuery.Vector);
    }

    [Fact]
    public async Task VectorOnly_Mode_Sends_No_Search_Text()
    {
        var service = ServiceWith(new RetrievalOptions { Mode = RetrievalMode.VectorOnly });

        await service.RetrieveAsync(Request(query: "elevation certificate requirements"));

        Assert.Null(_gateway.LastQuery!.SearchText);
        Assert.NotNull(_gateway.LastQuery.Vector);
    }

    [Fact]
    public async Task Searches_A_Pool_Wider_Than_The_Requested_TopK()
    {
        await _service.RetrieveAsync(Request(topK: 12));

        Assert.Equal(12 * AzureRetrievalService.CandidatePoolMultiplier, _gateway.LastQuery!.Size);
    }

    [Fact]
    public async Task Null_TopK_Falls_Back_To_The_Configured_Default()
    {
        var service = ServiceWith(new RetrievalOptions { DefaultTopK = 9 });

        await service.RetrieveAsync(Request(topK: null));

        Assert.Equal(9 * AzureRetrievalService.CandidatePoolMultiplier, _gateway.LastQuery!.Size);
    }

    [Fact]
    public async Task Null_TopK_Without_Options_Uses_The_Built_In_Default()
    {
        await _service.RetrieveAsync(Request(topK: null));

        Assert.Equal(
            RetrievalRequest.DefaultTopK * AzureRetrievalService.CandidatePoolMultiplier,
            _gateway.LastQuery!.Size);
    }

    [Fact]
    public async Task Explicit_TopK_Overrides_The_Configured_Default()
    {
        var service = ServiceWith(new RetrievalOptions { DefaultTopK = 9 });

        await service.RetrieveAsync(Request(topK: 2));

        Assert.Equal(2 * AzureRetrievalService.CandidatePoolMultiplier, _gateway.LastQuery!.Size);
    }

    /// <summary>
    /// The candidate pool is what the vector half of a hybrid query nominates
    /// before fusion (the gateway maps <c>Size</c> to
    /// <c>KNearestNeighborsCount</c> as well). Tying it to TopK starves fusion:
    /// a chunk that ranks second on keyword scoring can miss the results
    /// entirely because it was never nominated by the vector half.
    /// </summary>
    [Fact]
    public async Task Returns_Only_TopK_Chunks_From_The_Wider_Pool()
    {
        _gateway.HitsToReturn = [Hit("a"), Hit("b"), Hit("c"), Hit("d"), Hit("e")];

        var chunks = await _service.RetrieveAsync(Request(topK: 2));

        Assert.Equal(["a", "b"], chunks.Select(chunk => chunk.ChunkId));
    }

    [Fact]
    public async Task Returns_Every_Chunk_When_The_Pool_Yields_Fewer_Than_TopK()
    {
        _gateway.HitsToReturn = [Hit("a"), Hit("b")];

        var chunks = await _service.RetrieveAsync(Request(topK: 5));

        Assert.Equal(["a", "b"], chunks.Select(chunk => chunk.ChunkId));
    }

    [Fact]
    public async Task Widened_Pool_Never_Exceeds_The_Maximum_TopK()
    {
        await _service.RetrieveAsync(Request(topK: RetrievalRequest.MaxTopK));

        Assert.Equal(RetrievalRequest.MaxTopK, _gateway.LastQuery!.Size);
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

    private AzureRetrievalService ServiceWithReranking(
        FakeRerankingService reranking,
        RerankingOptions? options = null)
        => new(
            _gateway,
            _embeddingService,
            rerankingService: reranking,
            rerankingOptions: Options.Create(options ?? new RerankingOptions { Enabled = true }));

    private static ChunkSearchHit Hit(string chunkId)
        => new(ChunkDocument(chunkId: chunkId), 0.9);

    [Fact]
    public async Task Reranking_Widens_The_Search_To_The_Candidate_Pool_Size()
    {
        var service = ServiceWithReranking(
            new FakeRerankingService(),
            new RerankingOptions { Enabled = true, CandidatePoolSize = 20 });

        await service.RetrieveAsync(Request(topK: 5));

        Assert.Equal(20, _gateway.LastQuery!.Size);
    }

    [Fact]
    public async Task Reranking_Pool_Never_Shrinks_Below_The_Requested_TopK()
    {
        var service = ServiceWithReranking(
            new FakeRerankingService(),
            new RerankingOptions { Enabled = true, CandidatePoolSize = 3 });

        await service.RetrieveAsync(Request(topK: 10));

        Assert.Equal(10, _gateway.LastQuery!.Size);
    }

    [Fact]
    public async Task Reranking_Receives_The_Query_The_Candidates_And_The_Requested_TopK()
    {
        var reranking = new FakeRerankingService();
        var service = ServiceWithReranking(reranking);
        _gateway.HitsToReturn = [Hit("a"), Hit("b"), Hit("c")];

        await service.RetrieveAsync(Request(query: "elevation rules", topK: 2));

        var request = Assert.Single(reranking.ReceivedRequests);
        Assert.Equal("elevation rules", request.Query);
        Assert.Equal(["a", "b", "c"], request.Candidates.Select(chunk => chunk.ChunkId));
        Assert.Equal(2, request.TopN);
    }

    [Fact]
    public async Task Reranking_Determines_The_Final_Order_And_Count()
    {
        var service = ServiceWithReranking(new FakeRerankingService());
        _gateway.HitsToReturn = [Hit("a"), Hit("b"), Hit("c")];

        var chunks = await service.RetrieveAsync(Request(topK: 2));

        // The fake reranker reverses and trims, proving its output is returned.
        Assert.Equal(["c", "b"], chunks.Select(chunk => chunk.ChunkId));
    }

    [Fact]
    public async Task Disabled_Reranking_Keeps_The_Plain_Hybrid_Pipeline()
    {
        var reranking = new FakeRerankingService();
        var service = ServiceWithReranking(reranking, new RerankingOptions { Enabled = false });
        _gateway.HitsToReturn = [Hit("a"), Hit("b")];

        var chunks = await service.RetrieveAsync(Request(topK: 5));

        Assert.Equal(5 * AzureRetrievalService.CandidatePoolMultiplier, _gateway.LastQuery!.Size);
        Assert.Equal(["a", "b"], chunks.Select(chunk => chunk.ChunkId));
        Assert.Empty(reranking.ReceivedRequests);
    }

    [Fact]
    public async Task Reranking_Is_Skipped_When_No_Reranker_Is_Registered()
    {
        var service = new AzureRetrievalService(
            _gateway,
            _embeddingService,
            rerankingOptions: Options.Create(new RerankingOptions { Enabled = true }));
        _gateway.HitsToReturn = [Hit("a")];

        var chunks = await service.RetrieveAsync(Request(topK: 5));

        Assert.Equal(5 * AzureRetrievalService.CandidatePoolMultiplier, _gateway.LastQuery!.Size);
        Assert.Equal(["a"], chunks.Select(chunk => chunk.ChunkId));
    }

    [Fact]
    public async Task Reranking_Is_Skipped_When_Retrieval_Finds_Nothing()
    {
        var reranking = new FakeRerankingService();
        var service = ServiceWithReranking(reranking);

        var chunks = await service.RetrieveAsync(Request());

        Assert.Empty(chunks);
        Assert.Empty(reranking.ReceivedRequests);
    }
}
