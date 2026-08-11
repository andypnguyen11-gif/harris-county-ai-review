using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;
using HarrisCountyAI.UnitTests.Search.Retrieval;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Search.Reranking;

public class AzureSemanticRerankingServiceTests
{
    private readonly FakeSearchQueryGateway _gateway = new();

    private AzureSemanticRerankingService Service(RerankingOptions? options = null)
        => new(_gateway, Options.Create(options ?? new RerankingOptions { Enabled = true }));

    private static RetrievedChunk Chunk(string chunkId, double score = 0.5) => new()
    {
        ChunkId = chunkId,
        DocumentId = Guid.NewGuid(),
        Text = $"Text of {chunkId}",
        Title = "Floodplain Regulations",
        Score = score,
    };

    private static RerankingRequest Request(
        IReadOnlyList<RetrievedChunk> candidates,
        string query = "What does Section 4.2 require?",
        int topN = 3) => new()
        {
            Query = query,
            Candidates = candidates,
            TopN = topN,
        };

    private static ChunkSearchHit Hit(string chunkId, double? rerankerScore)
        => new(
            new SearchDocument { [SearchIndexDefinition.Fields.ChunkId] = chunkId },
            0.5,
            rerankerScore);

    [Fact]
    public async Task Reorders_Candidates_By_Reranker_Score()
    {
        var candidates = new[] { Chunk("a"), Chunk("b"), Chunk("c") };
        _gateway.HitsToReturn = [Hit("a", 1.1), Hit("b", 3.4), Hit("c", 2.2)];

        var reranked = await Service().RerankAsync(Request(candidates));

        Assert.Equal(["b", "c", "a"], reranked.Select(chunk => chunk.ChunkId));
    }

    [Fact]
    public async Task Captures_The_Reranker_Score_On_Each_Chunk()
    {
        var candidates = new[] { Chunk("a"), Chunk("b") };
        _gateway.HitsToReturn = [Hit("a", 1.25), Hit("b", 3.5)];

        var reranked = await Service().RerankAsync(Request(candidates));

        Assert.Equal(3.5, reranked[0].RerankerScore);
        Assert.Equal(1.25, reranked[1].RerankerScore);
    }

    [Fact]
    public async Task Keeps_The_Original_Retrieval_Score_On_Reranked_Chunks()
    {
        var candidates = new[] { Chunk("a", score: 0.87) };
        _gateway.HitsToReturn = [Hit("a", 2.0)];

        var reranked = await Service().RerankAsync(Request(candidates, topN: 1));

        Assert.Equal(0.87, reranked[0].Score);
    }

    [Fact]
    public async Task Limits_The_Result_To_TopN()
    {
        var candidates = Enumerable.Range(0, 20).Select(i => Chunk($"c{i}")).ToArray();
        _gateway.HitsToReturn = candidates.Select((c, i) => Hit(c.ChunkId, 4.0 - (i * 0.1))).ToList();

        var reranked = await Service().RerankAsync(Request(candidates, topN: 4));

        Assert.Equal(4, reranked.Count);
    }

    [Fact]
    public async Task Sends_A_Semantic_Keyword_Query_Over_The_Full_Candidate_Pool()
    {
        var candidates = new[] { Chunk("a"), Chunk("b"), Chunk("c") };
        var options = new RerankingOptions { Enabled = true, SemanticConfigurationName = "my-semantic-config" };

        await Service(options).RerankAsync(Request(candidates, query: "setback rules"));

        var query = Assert.Single(_gateway.ExecutedQueries);
        Assert.Equal("setback rules", query.SearchText);
        Assert.Null(query.Vector);
        Assert.Equal(3, query.Size);
        Assert.True(query.UseSemanticRanking);
        Assert.Equal("my-semantic-config", query.SemanticConfigurationName);
    }

    [Fact]
    public async Task Scopes_The_Rerank_Query_To_The_Corpus_And_The_Candidate_Chunks()
    {
        var candidates = new[] { Chunk("a-0001"), Chunk("b-0002") };

        await Service().RerankAsync(Request(candidates));

        Assert.Equal(
            "sourceType eq 'KnowledgeBase' and search.in(chunkId, 'a-0001,b-0002', ',')",
            _gateway.LastQuery!.Filter);
    }

    [Fact]
    public async Task Unscored_Candidates_Follow_Scored_Ones_In_Original_Order()
    {
        var candidates = new[] { Chunk("a"), Chunk("b"), Chunk("c"), Chunk("d") };
        _gateway.HitsToReturn = [Hit("c", 2.0)];

        var reranked = await Service().RerankAsync(Request(candidates, topN: 4));

        Assert.Equal(["c", "a", "b", "d"], reranked.Select(chunk => chunk.ChunkId));
        Assert.Equal(2.0, reranked[0].RerankerScore);
        Assert.All(reranked.Skip(1), chunk => Assert.Null(chunk.RerankerScore));
    }

    [Fact]
    public async Task Disabled_Reranking_Returns_The_Leading_Candidates_Without_Querying()
    {
        var candidates = new[] { Chunk("a"), Chunk("b"), Chunk("c") };

        var reranked = await Service(new RerankingOptions { Enabled = false })
            .RerankAsync(Request(candidates, topN: 2));

        Assert.Equal(["a", "b"], reranked.Select(chunk => chunk.ChunkId));
        Assert.All(reranked, chunk => Assert.Null(chunk.RerankerScore));
        Assert.Empty(_gateway.ExecutedQueries);
    }

    [Fact]
    public async Task Falls_Open_To_Hybrid_Order_When_The_Semantic_Query_Fails()
    {
        var candidates = new[] { Chunk("a"), Chunk("b"), Chunk("c") };
        _gateway.ExceptionToThrow = new InvalidOperationException(
            "Semantic search is not enabled for this service.");

        var reranked = await Service().RerankAsync(Request(candidates, topN: 2));

        Assert.Equal(["a", "b"], reranked.Select(chunk => chunk.ChunkId));
        Assert.All(reranked, chunk => Assert.Null(chunk.RerankerScore));
    }

    [Fact]
    public async Task Empty_Candidates_Return_Empty_Without_Querying()
    {
        var reranked = await Service().RerankAsync(Request([]));

        Assert.Empty(reranked);
        Assert.Empty(_gateway.ExecutedQueries);
    }

    [Fact]
    public async Task Rejects_A_Null_Request()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().RerankAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_An_Empty_Query(string query)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().RerankAsync(Request([Chunk("a")], query: query)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rejects_TopN_Below_One(int topN)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service().RerankAsync(Request([Chunk("a")], topN: topN)));

        Assert.Empty(_gateway.ExecutedQueries);
    }

    [Fact]
    public async Task Escapes_Single_Quotes_In_Chunk_Ids()
    {
        var candidates = new[] { Chunk("odd'id") };

        await Service().RerankAsync(Request(candidates, topN: 1));

        Assert.Contains("search.in(chunkId, 'odd''id', ',')", _gateway.LastQuery!.Filter);
    }
}
