using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// The /api/questions endpoint with the Both scope: one request produces two
/// separately scoped retrievals, the response tags each citation with the
/// corpus it came from, and a comparison missing either side reports
/// insufficient evidence instead of a one-sided answer.
/// </summary>
public class DualSourceQuestionsApiTests : IDisposable
{
    private const string Question = "Did the applicant submit everything the county requires?";
    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-555555555555");
    private static readonly Guid OtherCaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-666666666666");

    private readonly FakeRetrievalService _retrievalService = new();
    private readonly ScriptedLanguageModelService _languageModel = new();
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public DualSourceQuestionsApiTests()
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

    private static RetrievedChunk CountyChunk() => FakeRetrievalService.Chunk() with
    {
        ChunkId = "county-0001",
        Text = "A site plan is required with every development permit application.",
    };

    private static RetrievedChunk CaseChunk() => FakeRetrievalService.Chunk() with
    {
        ChunkId = "case-0001",
        DocumentId = Guid.Parse("d1d1d1d1-1111-1111-1111-d1d1d1d1d1d1"),
        Title = "application.pdf",
        Section = null,
        Page = 3,
        Department = null,
        PermitType = null,
        DocumentType = "PermitApplication",
        EffectiveDate = null,
        SourceUrl = null,
        Text = "Attached: site plan sheet 1 of 2.",
    };

    private void GiveBothSidesEvidence()
    {
        _retrievalService.ChunksByScope[SourceType.County] = [CountyChunk()];
        _retrievalService.ChunksByScope[SourceType.Case] = [CaseChunk()];
    }

    private Task<HttpResponseMessage> AskAsync(object body) =>
        _client.PostAsJsonAsync("/api/questions", body);

    [Fact]
    public async Task A_Comparison_Issues_One_Scoped_Retrieval_Per_Corpus()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A site plan is required and one was submitted.","citations":[1,2]}""");

        var response = await AskAsync(new { question = Question, scope = "Both", caseId = CaseId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _retrievalService.Requests.Count);
        Assert.Null(_retrievalService.RequestFor(SourceType.County).CaseId);
        Assert.Equal(CaseId, _retrievalService.RequestFor(SourceType.Case).CaseId);
    }

    [Fact]
    public async Task A_Comparison_Never_Retrieves_Another_Cases_Documents()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Compared.","citations":[1,2]}""");

        await AskAsync(new { question = Question, scope = "Both", caseId = CaseId });

        Assert.All(
            _retrievalService.Requests.Where(request => request.Scope == SourceType.Case),
            request => Assert.NotEqual(OtherCaseId, request.CaseId));
        Assert.DoesNotContain(_retrievalService.Requests, request => request.CaseId == OtherCaseId);
    }

    [Fact]
    public async Task Citations_Report_Which_Corpus_Each_Source_Came_From()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A site plan is required and one was submitted.","citations":[1,2]}""");

        var response = await AskAsync(new { question = Question, scope = "Both", caseId = CaseId });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("Answered", root.GetProperty("outcome").GetString());
        Assert.Equal("comparison-qa/v2", root.GetProperty("promptVersion").GetString());
        Assert.Equal(1, root.GetProperty("countyEvidenceCount").GetInt32());
        Assert.Equal(1, root.GetProperty("caseEvidenceCount").GetInt32());

        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        Assert.Equal(2, citations.Count);

        var county = Assert.Single(citations, citation => citation.GetProperty("source").GetString() == "County");
        Assert.Equal("Floodplain Regulations", county.GetProperty("title").GetString());

        var caseCitation = Assert.Single(
            citations, citation => citation.GetProperty("source").GetString() == "Case");
        Assert.Equal("application.pdf", caseCitation.GetProperty("title").GetString());
        Assert.Equal(3, caseCitation.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task A_Comparison_Without_A_Case_Id_Is_Rejected()
    {
        var response = await AskAsync(new { question = Question, scope = "Both" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("caseId", out _));
        Assert.Empty(_retrievalService.Requests);
    }

    [Fact]
    public async Task The_Both_Scope_Is_Parsed_Case_Insensitively()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Compared.","citations":[1,2]}""");

        var response = await AskAsync(new { question = Question, scope = "both", caseId = CaseId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _retrievalService.Requests.Count);
    }

    [Fact]
    public async Task A_Comparison_With_County_Evidence_Only_Reports_Insufficient_Evidence()
    {
        _retrievalService.ChunksByScope[SourceType.County] = [CountyChunk()];
        _retrievalService.ChunksByScope[SourceType.Case] = [];

        var response = await AskAsync(new { question = Question, scope = "Both", caseId = CaseId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InsufficientEvidence", body.RootElement.GetProperty("outcome").GetString());
        Assert.Empty(body.RootElement.GetProperty("citations").EnumerateArray());
        Assert.Empty(_languageModel.Requests);
    }

    [Fact]
    public async Task A_Comparison_With_Case_Evidence_Only_Reports_Insufficient_Evidence()
    {
        _retrievalService.ChunksByScope[SourceType.County] = [];
        _retrievalService.ChunksByScope[SourceType.Case] = [CaseChunk()];

        var response = await AskAsync(new { question = Question, scope = "Both", caseId = CaseId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InsufficientEvidence", body.RootElement.GetProperty("outcome").GetString());
        Assert.Empty(_languageModel.Requests);
    }

    [Fact]
    public async Task A_Model_Failure_Surfaces_As_502_Rather_Than_An_Unverifiable_Comparison()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueException(new HttpRequestException("model unavailable"));

        var response = await AskAsync(new { question = Question, scope = "Both", caseId = CaseId });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
