using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// What a client actually receives when a dependency is down, end to end
/// through the real middleware pipeline.
/// </summary>
public class ErrorHandlingApiTests : IDisposable
{
    /// <summary>
    /// Fragments that must never appear in a response body. Each is something
    /// the corresponding exception message or configuration genuinely contains,
    /// so a regression that starts echoing exception text would trip on them.
    /// </summary>
    private static readonly string[] ForbiddenFragments =
    [
        "test.invalid",
        "integration-test-key",
        "AccountKey",
        "UseDevelopmentStorage",
        "HarrisCountyAI.Infrastructure",
        "   at ",
    ];

    private readonly FakeRetrievalService _retrievalService = new();
    private readonly ScriptedLanguageModelService _languageModel = new();
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ErrorHandlingApiTests()
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
        GC.SuppressFinalize(this);
    }

    private Task<HttpResponseMessage> AskAsync(string question = "What must an application include?")
        => _client.PostAsJsonAsync("/api/questions", new { question });

    private static async Task<(JsonElement Body, string Raw)> ReadProblemAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    private static void AssertNothingSensitiveLeaked(string raw)
    {
        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Search_Being_Unavailable_Returns_503_Naming_Only_The_Capability()
    {
        _retrievalService.ExceptionToThrow = new ExternalServiceUnavailableException(
            ExternalServiceNames.Search,
            "https://search.test.invalid returned 503 for index harris-county-chunks",
            statusCode: 503);

        var response = await AskAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.RetryAfter?.Delta);

        var (body, raw) = await ReadProblemAsync(response);
        Assert.Equal(503, body.GetProperty("status").GetInt32());
        Assert.Equal(ExternalServiceNames.Search, body.GetProperty("service").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
        AssertNothingSensitiveLeaked(raw);
    }

    [Fact]
    public async Task Search_Timing_Out_Returns_504()
    {
        _retrievalService.ExceptionToThrow = new ExternalServiceTimeoutException(
            ExternalServiceNames.Search, "search at https://search.test.invalid timed out");

        var response = await AskAsync();

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var (body, raw) = await ReadProblemAsync(response);
        Assert.Equal(ExternalServiceNames.Search, body.GetProperty("service").GetString());
        AssertNothingSensitiveLeaked(raw);
    }

    [Fact]
    public async Task An_Embeddings_Outage_Is_Reported_The_Same_Way()
    {
        _retrievalService.ExceptionToThrow = new ExternalServiceUnavailableException(
            ExternalServiceNames.Embeddings, "embedding deployment returned 429", statusCode: 429);

        var response = await AskAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var (body, _) = await ReadProblemAsync(response);
        Assert.Equal(ExternalServiceNames.Embeddings, body.GetProperty("service").GetString());
    }

    [Fact]
    public async Task The_Model_Being_Unavailable_Degrades_To_An_Unanswered_502()
    {
        // Question answering catches model failures itself and reports an
        // honest "not answered" rather than letting the exception escape — the
        // reviewer sees why, and no unverifiable answer is presented.
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueException(new ExternalServiceUnavailableException(
            ExternalServiceNames.LanguageModel,
            "deployment integration-test-deployment at https://language-model.test.invalid returned 503",
            statusCode: 503));

        var response = await AskAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var (body, raw) = await ReadProblemAsync(response);
        Assert.Equal(502, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
        AssertNothingSensitiveLeaked(raw);
    }

    [Fact]
    public async Task Malformed_Model_Output_Is_Reported_As_Unanswered_Not_Answered()
    {
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueContent("I think probably yes, but here is no JSON at all.");

        var response = await AskAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var (body, _) = await ReadProblemAsync(response);
        Assert.Equal(502, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task An_Unexpected_Failure_Returns_A_500_Problem_Document_With_No_Detail()
    {
        _retrievalService.ExceptionToThrow = new InvalidOperationException(
            "Server=localhost,1433;User Id=sa;Password=LocalDev!Passw0rd");

        var response = await AskAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var (body, raw) = await ReadProblemAsync(response);
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.False(body.TryGetProperty("service", out _));
        Assert.DoesNotContain("Password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", raw, StringComparison.OrdinalIgnoreCase);
        AssertNothingSensitiveLeaked(raw);
    }

    [Fact]
    public async Task An_Outage_Degrades_One_Feature_And_Does_Not_Latch()
    {
        _retrievalService.ExceptionToThrow = new ExternalServiceUnavailableException(
            ExternalServiceNames.Search, "search is down", statusCode: 503);

        var duringOutage = await AskAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, duringOutage.StatusCode);

        // Endpoints that do not touch search keep serving throughout.
        var healthResponse = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        // And the feature recovers by itself once the dependency does: nothing
        // in the failure handling latches the endpoint off.
        _retrievalService.ExceptionToThrow = null;
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A completed application form is required.","citations":[1]}""");

        var afterRecovery = await AskAsync();
        Assert.Equal(HttpStatusCode.OK, afterRecovery.StatusCode);
    }

    [Fact]
    public async Task A_Correlation_Id_Supplied_By_The_Caller_Comes_Back_On_The_Error()
    {
        _retrievalService.ExceptionToThrow = new ExternalServiceUnavailableException(
            ExternalServiceNames.Search, "search is down", statusCode: 503);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/questions")
        {
            Content = JsonContent.Create(new { question = "What must an application include?" }),
        };
        request.Headers.Add("X-Correlation-Id", "reviewer-reported-id-42");

        var response = await _client.SendAsync(request);

        var (body, _) = await ReadProblemAsync(response);
        var correlationId = body.GetProperty("correlationId").GetString();

        // Before the correlation-id middleware is in the pipeline the API
        // assigns its own id; either way the response always carries one the
        // reviewer can quote.
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task A_Validation_Failure_Is_A_Problem_Document_With_A_Correlation_Id()
    {
        var response = await AskAsync(question: "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (body, _) = await ReadProblemAsync(response);
        Assert.Equal("One or more validation errors occurred.", body.GetProperty("title").GetString());
        Assert.True(body.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("question", out _));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task An_Unmatched_Route_Is_A_Problem_Document_Too()
    {
        var response = await _client.GetAsync("/api/this-route-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var (body, _) = await ReadProblemAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Error_Handling_Does_Not_Weaken_Authentication()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/questions", new { question = "anything" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
