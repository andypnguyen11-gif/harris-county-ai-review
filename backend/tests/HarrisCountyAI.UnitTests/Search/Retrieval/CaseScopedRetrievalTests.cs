using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;
using HarrisCountyAI.UnitTests.QuestionAnswering;
using HarrisCountyAI.UnitTests.Search.Reranking;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Search.Retrieval;

/// <summary>
/// Case-scoped retrieval: every query must carry both the CaseDocument source
/// filter and the exact case id, a case-scoped request without a case id is
/// rejected outright, and reranking can never widen the scope.
/// </summary>
public class CaseScopedRetrievalTests
{
    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-111111111111");

    private readonly FakeSearchQueryGateway _gateway = new();
    private readonly FakeEmbeddingService _embeddingService = new();
    private readonly AzureRetrievalService _service;

    public CaseScopedRetrievalTests()
    {
        _service = new AzureRetrievalService(_gateway, _embeddingService);
    }

    private static RetrievalRequest CaseRequest(Guid? caseId = null) => new()
    {
        Query = "Who signed this application?",
        Scope = SourceType.Case,
        CaseId = caseId ?? CaseId,
    };

    [Fact]
    public async Task Case_Scope_Filters_To_The_CaseDocument_Source_And_The_Exact_Case()
    {
        await _service.RetrieveAsync(CaseRequest());

        Assert.Equal(
            $"sourceType eq 'CaseDocument' and caseId eq '{CaseId:D}'",
            _gateway.LastQuery!.Filter);
    }

    [Fact]
    public async Task Case_Scope_Never_Applies_Corpus_Metadata_Filters()
    {
        await _service.RetrieveAsync(CaseRequest() with
        {
            Department = "Engineering",
            PermitType = "FloodplainDevelopmentPermit",
            DocumentType = "Regulation",
        });

        Assert.Equal(
            $"sourceType eq 'CaseDocument' and caseId eq '{CaseId:D}'",
            _gateway.LastQuery!.Filter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Case_Scope_Without_A_Case_Id_Is_Rejected_Before_Any_Query(string? caseId)
    {
        var request = new RetrievalRequest
        {
            Query = "Who signed this application?",
            Scope = SourceType.Case,
            CaseId = caseId is null ? null : Guid.Parse(caseId),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RetrieveAsync(request));

        Assert.Empty(_gateway.ExecutedQueries);
        Assert.Empty(_embeddingService.ReceivedBatches);
    }

    [Fact]
    public async Task County_Scope_Ignores_A_Supplied_Case_Id()
    {
        await _service.RetrieveAsync(new RetrievalRequest
        {
            Query = "What does the county require?",
            Scope = SourceType.County,
            CaseId = CaseId,
        });

        Assert.Equal("sourceType eq 'KnowledgeBase'", _gateway.LastQuery!.Filter);
    }

    [Fact]
    public void The_Case_Filter_Uses_The_Lowercase_D_Guid_Format_The_Index_Stores()
    {
        var filter = AzureRetrievalService.BuildCaseFilter(CaseRequest());

        Assert.Contains(CaseId.ToString("D"), filter);
        Assert.DoesNotContain(CaseId.ToString("N"), filter);
    }

    [Fact]
    public async Task Reranking_Receives_The_Case_Scope_Filter()
    {
        var reranking = new FakeRerankingService();
        var service = new AzureRetrievalService(
            _gateway,
            _embeddingService,
            rerankingService: reranking,
            rerankingOptions: Options.Create(new RerankingOptions { Enabled = true }));
        _gateway.HitsToReturn = [CaseHit()];

        await service.RetrieveAsync(CaseRequest());

        var request = Assert.Single(reranking.ReceivedRequests);
        Assert.Equal($"sourceType eq 'CaseDocument' and caseId eq '{CaseId:D}'", request.ScopeFilter);
    }

    [Fact]
    public void The_Semantic_Reranker_Keeps_The_Case_Scope_Filter_In_Its_Query()
    {
        var request = new RerankingRequest
        {
            Query = "Who signed this application?",
            Candidates = [FakeRetrievalService.Chunk(chunkId: "case-chunk-0000")],
            TopN = 1,
            ScopeFilter = $"sourceType eq 'CaseDocument' and caseId eq '{CaseId:D}'",
        };

        var filter = AzureSemanticRerankingService.BuildCandidateFilter(request);

        Assert.StartsWith($"sourceType eq 'CaseDocument' and caseId eq '{CaseId:D}' and ", filter);
        Assert.Contains("search.in(chunkId, 'case-chunk-0000', ',')", filter);
        Assert.DoesNotContain("KnowledgeBase", filter);
    }

    [Fact]
    public void The_Semantic_Reranker_Defaults_To_The_Corpus_Filter_Without_A_Scope_Filter()
    {
        var request = new RerankingRequest
        {
            Query = "What does the county require?",
            Candidates = [FakeRetrievalService.Chunk(chunkId: "corpus-chunk-0000")],
            TopN = 1,
        };

        var filter = AzureSemanticRerankingService.BuildCandidateFilter(request);

        Assert.StartsWith("sourceType eq 'KnowledgeBase' and ", filter);
    }

    private static ChunkSearchHit CaseHit()
    {
        var document = new global::Azure.Search.Documents.Models.SearchDocument
        {
            [SearchIndexDefinition.Fields.ChunkId] = "case-chunk-0000",
            [SearchIndexDefinition.Fields.DocumentId] = Guid.NewGuid().ToString("D"),
            [SearchIndexDefinition.Fields.Text] = "Signed by Jane Doe.",
            [SearchIndexDefinition.Fields.Title] = "application.pdf",
        };
        return new ChunkSearchHit(document, 0.9);
    }
}
