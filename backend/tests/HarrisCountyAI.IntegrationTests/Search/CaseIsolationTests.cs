using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.IntegrationTests.Search;

/// <summary>
/// Proves the separation guarantee of the shared chunk index end to end
/// through the real indexing and retrieval services: corpus queries can never
/// see case-document chunks, and case-scoped queries can never see corpus
/// chunks or another case's chunks (see docs/architecture/rag-architecture.md).
/// </summary>
public class CaseIsolationTests
{
    private static readonly Guid CaseA = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid CaseB = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    private static readonly Guid CorpusDocumentId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
    private static readonly Guid CaseADocumentId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");
    private static readonly Guid CaseBDocumentId = Guid.Parse("eeeeeeee-5555-5555-5555-555555555555");

    private readonly InMemorySearchIndex _index = new();
    private readonly AzureDocumentIndexService _indexService;
    private readonly AzureRetrievalService _retrievalService;

    public CaseIsolationTests()
    {
        var options = Options.Create(new SearchOptions { IndexName = "isolation-test-chunks" });
        _indexService = new AzureDocumentIndexService(_index, options);
        _retrievalService = new AzureRetrievalService(_index, new StubEmbeddingService());
    }

    private async Task IndexAllCorporaAsync()
    {
        await _indexService.IndexAsync(
        [
            Chunk(CorpusDocumentId, IndexSourceTypes.KnowledgeBase, "Floodplain Regulations", caseId: null),
            Chunk(CaseADocumentId, IndexSourceTypes.CaseDocument, "case-a-site-plan.pdf", CaseA),
            Chunk(CaseBDocumentId, IndexSourceTypes.CaseDocument, "case-b-site-plan.pdf", CaseB),
        ]);
    }

    [Fact]
    public async Task Corpus_Retrieval_Never_Returns_Case_Chunks()
    {
        await IndexAllCorporaAsync();

        var chunks = await _retrievalService.RetrieveAsync(
            new RetrievalRequest { Query = "site plan requirements", TopK = 50 });

        var chunk = Assert.Single(chunks);
        Assert.Equal(CorpusDocumentId, chunk.DocumentId);
        Assert.DoesNotContain(chunks, c => c.DocumentId == CaseADocumentId);
        Assert.DoesNotContain(chunks, c => c.DocumentId == CaseBDocumentId);
    }

    [Fact]
    public async Task Case_Scoped_Queries_Never_Return_Corpus_Chunks_Or_Another_Cases_Chunks()
    {
        await IndexAllCorporaAsync();

        // The documented case-evidence filter shape (rag-architecture.md):
        // sourceType eq 'CaseDocument' and caseId eq '<case guid>'.
        var hits = await _index.SearchAsync(
            new ChunkSearchQuery
            {
                Filter = $"{SearchIndexDefinition.Fields.SourceType} eq '{IndexSourceTypes.CaseDocument}'"
                    + $" and {SearchIndexDefinition.Fields.CaseId} eq '{CaseA:D}'",
                Size = 50,
            },
            CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal(
            CaseADocumentId.ToString("D"),
            hit.Document[SearchIndexDefinition.Fields.DocumentId]);
    }

    [Fact]
    public async Task A_Corpus_Chunk_Can_Never_Satisfy_A_Case_Filter_Because_Its_CaseId_Is_Null()
    {
        await _indexService.IndexAsync(
            [Chunk(CorpusDocumentId, IndexSourceTypes.KnowledgeBase, "Floodplain Regulations", caseId: null)]);

        var hits = await _index.SearchAsync(
            new ChunkSearchQuery
            {
                Filter = $"{SearchIndexDefinition.Fields.CaseId} eq '{CaseA:D}'",
                Size = 50,
            },
            CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Corpus_Retrieval_Applies_The_KnowledgeBase_Source_Filter_To_Every_Query()
    {
        await IndexAllCorporaAsync();

        await _retrievalService.RetrieveAsync(new RetrievalRequest { Query = "anything" });

        var query = Assert.Single(_index.ExecutedQueries);
        Assert.Contains(
            $"{SearchIndexDefinition.Fields.SourceType} eq '{IndexSourceTypes.KnowledgeBase}'",
            query.Filter);
    }

    [Fact]
    public async Task Deleting_A_Case_Document_Removes_Its_Chunks_And_Nothing_Else()
    {
        await IndexAllCorporaAsync();

        await _indexService.DeleteDocumentAsync(CaseADocumentId);

        var remaining = _index.Documents
            .Select(document => (string)document[SearchIndexDefinition.Fields.DocumentId])
            .ToList();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(CaseADocumentId.ToString("D"), remaining);
        Assert.Contains(CorpusDocumentId.ToString("D"), remaining);
        Assert.Contains(CaseBDocumentId.ToString("D"), remaining);
    }

    [Fact]
    public async Task Reindexing_A_Case_Document_Replaces_Its_Chunks_Without_Leaving_Stale_Ones()
    {
        await IndexAllCorporaAsync();

        // Delete-then-index, the sequence CaseDocumentIndexingService runs.
        await _indexService.DeleteDocumentAsync(CaseADocumentId);
        await _indexService.IndexAsync(
            [Chunk(CaseADocumentId, IndexSourceTypes.CaseDocument, "case-a-site-plan-v2.pdf", CaseA, sequence: 7)]);

        var caseAChunks = _index.Documents
            .Where(document => (string)document[SearchIndexDefinition.Fields.DocumentId] == CaseADocumentId.ToString("D"))
            .ToList();
        var document = Assert.Single(caseAChunks);
        Assert.Equal("case-a-site-plan-v2.pdf", document[SearchIndexDefinition.Fields.Title]);
    }

    private static IndexableChunk Chunk(
        Guid documentId,
        string sourceType,
        string title,
        Guid? caseId,
        int sequence = 0) => new()
        {
            ChunkId = $"{documentId:N}-{sequence:D4}",
            DocumentId = documentId,
            Sequence = sequence,
            Text = $"Text of {title}.",
            SourceType = sourceType,
            Title = title,
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
