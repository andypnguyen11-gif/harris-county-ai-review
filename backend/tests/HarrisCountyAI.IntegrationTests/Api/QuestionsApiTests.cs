using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Api;

public class QuestionsApiTests : IDisposable
{
    private readonly FakeRetrievalService _retrievalService = new();
    private readonly ScriptedLanguageModelService _languageModel = new();
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public QuestionsApiTests()
    {
        _factory = new TestApplicationFactory
        {
            // Question answering is not wired into the composition root yet;
            // register the real service with faked retrieval and model, the
            // same shape the coordinator wiring will produce.
            TestServices = services =>
            {
                services.AddSingleton<IRetrievalService>(_retrievalService);
                services.AddSingleton<ILanguageModelService>(_languageModel);
                services.AddQuestionAnswering();
            },
        };
        _client = _factory.CreateClient().WithToken(
            TestAuthentication.CreateToken(TestAuthentication.ReviewerUsername, ["Reviewer"]));
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private Task<HttpResponseMessage> AskAsync(string question)
        => _client.PostAsJsonAsync("/api/questions", new { question });

    [Fact]
    public async Task Ask_Returns_A_Grounded_Answer_With_Citations()
    {
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A completed application form is required.","citations":[1]}""");

        var response = await AskAsync("What must a floodplain permit application include?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("Answered", root.GetProperty("outcome").GetString());
        Assert.Equal("A completed application form is required.", root.GetProperty("answer").GetString());
        var citation = Assert.Single(root.GetProperty("citations").EnumerateArray());
        Assert.Equal(1, citation.GetProperty("number").GetInt32());
        Assert.Equal("Floodplain Regulations", citation.GetProperty("title").GetString());
        Assert.Equal("Section 4.2", citation.GetProperty("section").GetString());
        Assert.Equal(17, citation.GetProperty("page").GetInt32());
        Assert.Equal("https://www.hcfcd.org/regulations", citation.GetProperty("sourceUrl").GetString());
        Assert.Equal("corpus-qa/v1", root.GetProperty("promptVersion").GetString());
    }

    [Fact]
    public async Task Ask_Returns_InsufficientEvidence_When_Nothing_Is_Retrieved()
    {
        _retrievalService.ChunksToReturn = [];

        var response = await AskAsync("What is the airspeed velocity of an unladen swallow?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InsufficientEvidence", body.RootElement.GetProperty("outcome").GetString());
        Assert.Empty(body.RootElement.GetProperty("citations").EnumerateArray());
        Assert.Empty(_languageModel.Requests);
    }

    [Fact]
    public async Task Ask_Returns_InsufficientEvidence_When_The_Model_Reports_It()
    {
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueContent(
            """{"status":"insufficient_evidence","answer":"The sources do not address fees.","citations":[]}""");

        var response = await AskAsync("What are the permit fees?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InsufficientEvidence", body.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("The sources do not address fees.", body.RootElement.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task Ask_Rejects_A_Missing_Question()
    {
        var response = await _client.PostAsJsonAsync("/api/questions", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("question", out _));
        Assert.Empty(_retrievalService.Requests);
    }

    [Fact]
    public async Task Ask_Rejects_An_Overlong_Question()
    {
        var response = await AskAsync(new string('q', QuestionRequest.MaxQuestionLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_retrievalService.Requests);
    }

    [Fact]
    public async Task Ask_Returns_502_When_The_Model_Fails()
    {
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueException(new InvalidOperationException("deployment down"));

        var response = await AskAsync("What must a floodplain permit application include?");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("The question could not be answered.", body.RootElement.GetProperty("title").GetString());
    }
}
