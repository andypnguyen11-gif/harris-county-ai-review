using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.IntegrationTests.Search;

/// <summary>
/// Cross-case security: a question asked about case A can never retrieve
/// case B's documents (or the corpus), no matter how relevant they are. The
/// tests run the real indexing and retrieval services end to end over the
/// in-memory index, so the exact filters that ship are what is being proved.
/// </summary>
public class CrossCaseSecurityTests
{
    private static readonly Guid CaseA = Guid.Parse("aaaaaaaa-6666-6666-6666-666666666666");
    private static readonly Guid CaseB = Guid.Parse("bbbbbbbb-7777-7777-7777-777777777777");
    private static readonly Guid CaseADocumentId = Guid.Parse("dddddddd-8888-8888-8888-888888888888");
    private static readonly Guid CaseBDocumentId = Guid.Parse("eeeeeeee-9999-9999-9999-999999999999");
    private static readonly Guid CorpusDocumentId = Guid.Parse("cccccccc-aaaa-aaaa-aaaa-cccccccccccc");

    private readonly InMemorySearchIndex _index = new();
    private readonly AzureRetrievalService _retrievalService;

    public CrossCaseSecurityTests()
    {
        var indexService = new AzureDocumentIndexService(
            _index, Options.Create(new SearchOptions { IndexName = "cross-case-test-chunks" }));
        _retrievalService = new AzureRetrievalService(_index, new StubEmbeddingService());

        indexService.IndexAsync(
        [
            Chunk(CorpusDocumentId, IndexSourceTypes.KnowledgeBase, "Floodplain Regulations", caseId: null,
                text: "A drainage plan is required for every submission."),
            Chunk(CaseADocumentId, IndexSourceTypes.CaseDocument, "case-a-application.pdf", CaseA,
                text: "Case A: signed by Jane Doe, drainage plan attached."),
            Chunk(CaseBDocumentId, IndexSourceTypes.CaseDocument, "case-b-application.pdf", CaseB,
                text: "Case B: signed by John Roe, drainage plan attached."),
        ]).GetAwaiter().GetResult();
    }

    private Task<IReadOnlyList<RetrievedChunk>> RetrieveForCaseAsync(Guid caseId)
        => _retrievalService.RetrieveAsync(new RetrievalRequest
        {
            Query = "drainage plan", // Relevant to every indexed chunk.
            Scope = SourceType.Case,
            CaseId = caseId,
            TopK = 50,
        });

    [Fact]
    public async Task A_Case_A_Question_Only_Ever_Sees_Case_A_Chunks()
    {
        var chunks = await RetrieveForCaseAsync(CaseA);

        var chunk = Assert.Single(chunks);
        Assert.Equal(CaseADocumentId, chunk.DocumentId);
        Assert.DoesNotContain(chunks, c => c.DocumentId == CaseBDocumentId);
        Assert.DoesNotContain(chunks, c => c.DocumentId == CorpusDocumentId);
    }

    [Fact]
    public async Task A_Case_B_Question_Only_Ever_Sees_Case_B_Chunks()
    {
        var chunks = await RetrieveForCaseAsync(CaseB);

        var chunk = Assert.Single(chunks);
        Assert.Equal(CaseBDocumentId, chunk.DocumentId);
    }

    [Fact]
    public async Task A_Case_With_No_Documents_Retrieves_Nothing_Rather_Than_Someone_Elses()
    {
        var chunks = await RetrieveForCaseAsync(Guid.Parse("ffffffff-0000-0000-0000-00000000ffff"));

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task Every_Case_Query_Carries_Both_The_Source_And_The_CaseId_Filter()
    {
        await RetrieveForCaseAsync(CaseA);

        var query = Assert.Single(_index.ExecutedQueries);
        Assert.Equal(
            $"sourceType eq 'CaseDocument' and caseId eq '{CaseA:D}'",
            query.Filter);
    }

    [Fact]
    public async Task Case_Chunks_Surface_Their_Document_And_Page_For_Citations()
    {
        var chunks = await RetrieveForCaseAsync(CaseA);

        var chunk = Assert.Single(chunks);
        Assert.Equal("case-a-application.pdf", chunk.Title);
        Assert.Equal(2, chunk.Page);
        Assert.Equal(nameof(HarrisCountyAI.Domain.Enums.DocumentType.PermitApplication), chunk.DocumentType);
    }

    [Fact]
    public async Task Corpus_Questions_Still_Never_See_Any_Case_Chunk()
    {
        var chunks = await _retrievalService.RetrieveAsync(
            new RetrievalRequest { Query = "drainage plan", TopK = 50 });

        var chunk = Assert.Single(chunks);
        Assert.Equal(CorpusDocumentId, chunk.DocumentId);
    }

    private static IndexableChunk Chunk(
        Guid documentId,
        string sourceType,
        string title,
        Guid? caseId,
        string text) => new()
        {
            ChunkId = $"{documentId:N}-0000",
            DocumentId = documentId,
            Sequence = 0,
            Text = text,
            SourceType = sourceType,
            Title = title,
            PageNumber = 2,
            DocumentType = caseId is null ? "Regulation" : "PermitApplication",
            CaseId = caseId,
            Embedding = MakeVector(),
        };

    private static float[] MakeVector()
    {
        var vector = new float[SearchIndexDefinition.EmbeddingDimensions];
        vector[0] = 1f;
        return vector;
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                [.. inputs.Select((_, index) => new EmbeddingResult(MakeVector(), index, "stub-embedding-model"))]);
    }
}
