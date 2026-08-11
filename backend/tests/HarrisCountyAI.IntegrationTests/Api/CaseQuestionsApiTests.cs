using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// The /api/questions endpoint with the Case scope: scope and case id flow
/// into retrieval, a case-scoped question without a case id is rejected, and
/// case citations surface the document and page a reviewer needs to verify
/// the answer.
/// </summary>
public class CaseQuestionsApiTests : IDisposable
{
    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-333333333333");

    private readonly FakeRetrievalService _retrievalService = new();
    private readonly ScriptedLanguageModelService _languageModel = new();
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public CaseQuestionsApiTests()
    {
        _factory = new TestApplicationFactory
        {
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

    private static RetrievedChunk CaseChunk() => FakeRetrievalService.Chunk() with
    {
        DocumentId = Guid.Parse("d1d1d1d1-1111-1111-1111-d1d1d1d1d1d1"),
        Title = "application.pdf",
        Section = null,
        Page = 2,
        Department = null,
        PermitType = null,
        DocumentType = "PermitApplication",
        EffectiveDate = null,
        SourceUrl = null,
        Text = "Signed by Jane Doe on page 2.",
    };

    [Fact]
    public async Task A_Case_Question_Passes_The_Scope_And_Case_Id_To_Retrieval()
    {
        _retrievalService.ChunksToReturn = [CaseChunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Jane Doe signed the application.","citations":[1]}""");

        var response = await _client.PostAsJsonAsync(
            "/api/questions",
            new { question = "Who signed this application?", scope = "Case", caseId = CaseId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(_retrievalService.Requests);
        Assert.Equal(SourceType.Case, request.Scope);
        Assert.Equal(CaseId, request.CaseId);
    }

    [Fact]
    public async Task A_Case_Answer_Cites_The_Document_And_Page()
    {
        _retrievalService.ChunksToReturn = [CaseChunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Jane Doe signed the application.","citations":[1]}""");

        var response = await _client.PostAsJsonAsync(
            "/api/questions",
            new { question = "Who signed this application?", scope = "Case", caseId = CaseId });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("Answered", root.GetProperty("outcome").GetString());
        Assert.Equal("case-qa/v1", root.GetProperty("promptVersion").GetString());
        var citation = Assert.Single(root.GetProperty("citations").EnumerateArray());
        Assert.Equal("application.pdf", citation.GetProperty("title").GetString());
        Assert.Equal(2, citation.GetProperty("page").GetInt32());
        Assert.Equal("d1d1d1d1-1111-1111-1111-d1d1d1d1d1d1", citation.GetProperty("documentId").GetString());
    }

    [Fact]
    public async Task A_Case_Question_Without_A_Case_Id_Is_Rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/questions",
            new { question = "Who signed this application?", scope = "Case" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("caseId", out _));
        Assert.Empty(_retrievalService.Requests);
    }

    [Fact]
    public async Task An_Unknown_Scope_Is_Rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/questions",
            new { question = "Who signed this application?", scope = "Everything" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("scope", out _));
        Assert.Empty(_retrievalService.Requests);
    }

    [Fact]
    public async Task The_Scope_Is_Parsed_Case_Insensitively()
    {
        _retrievalService.ChunksToReturn = [];

        var response = await _client.PostAsJsonAsync(
            "/api/questions",
            new { question = "Who signed this application?", scope = "case", caseId = CaseId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SourceType.Case, Assert.Single(_retrievalService.Requests).Scope);
    }

    [Fact]
    public async Task Questions_Without_A_Scope_Stay_County_Scoped()
    {
        _retrievalService.ChunksToReturn = [];

        var response = await _client.PostAsJsonAsync(
            "/api/questions",
            new { question = "What does the county require?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(_retrievalService.Requests);
        Assert.Equal(SourceType.County, request.Scope);
        Assert.Null(request.CaseId);
    }

    [Fact]
    public async Task A_Case_Question_With_No_Evidence_Reports_Insufficient_Evidence()
    {
        _retrievalService.ChunksToReturn = [];

        var response = await _client.PostAsJsonAsync(
            "/api/questions",
            new { question = "Who signed this application?", scope = "Case", caseId = CaseId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InsufficientEvidence", body.RootElement.GetProperty("outcome").GetString());
        Assert.Empty(_languageModel.Requests);
    }
}
