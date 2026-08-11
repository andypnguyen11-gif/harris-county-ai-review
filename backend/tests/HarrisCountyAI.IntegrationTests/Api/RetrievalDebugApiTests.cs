using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Api;

public class RetrievalDebugApiTests : IDisposable
{
    private readonly FakeRetrievalService _retrievalService = new();
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public RetrievalDebugApiTests()
    {
        _factory = new TestApplicationFactory
        {
            // Corpus retrieval is not wired into the composition root yet;
            // register the service the debug endpoint depends on directly.
            TestServices = services => services.AddSingleton<IRetrievalService>(_retrievalService),
        };
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Search_Returns_The_Retrieved_Chunks()
    {
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];

        var response = await _client.PostAsJsonAsync(
            "/api/debug/retrieval",
            new { query = "What must a floodplain permit include?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var chunk = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("0f8fad5bd9cb469fa165408319b0e0d9-0000", chunk.GetProperty("chunkId").GetString());
        Assert.Equal("Floodplain Regulations", chunk.GetProperty("title").GetString());
        Assert.Equal("Section 4.2", chunk.GetProperty("section").GetString());
        Assert.Equal(17, chunk.GetProperty("page").GetInt32());
        Assert.Equal(0.9, chunk.GetProperty("score").GetDouble());
    }

    [Fact]
    public async Task Search_Passes_Query_TopK_And_Filters_To_The_Service()
    {
        var response = await _client.PostAsJsonAsync("/api/debug/retrieval", new
        {
            query = "drainage requirements",
            topK = 3,
            department = "Engineering",
            permitType = "FloodplainDevelopmentPermit",
            documentType = "Regulation",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = _retrievalService.LastRequest;
        Assert.NotNull(request);
        Assert.Equal("drainage requirements", request.Query);
        Assert.Equal(3, request.TopK);
        Assert.Equal("Engineering", request.Department);
        Assert.Equal("FloodplainDevelopmentPermit", request.PermitType);
        Assert.Equal("Regulation", request.DocumentType);
    }

    [Fact]
    public async Task Search_Defaults_TopK_When_Omitted()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/debug/retrieval",
            new { query = "drainage requirements" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RetrievalRequest.DefaultTopK, _retrievalService.LastRequest!.TopK);
    }

    [Fact]
    public async Task Search_Rejects_A_Missing_Query()
    {
        var response = await _client.PostAsJsonAsync("/api/debug/retrieval", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("query", out _));
        Assert.Empty(_retrievalService.Requests);
    }

    [Fact]
    public async Task Search_Rejects_TopK_Out_Of_Range()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/debug/retrieval",
            new { query = "drainage requirements", topK = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("topK", out _));
        Assert.Empty(_retrievalService.Requests);
    }
}
