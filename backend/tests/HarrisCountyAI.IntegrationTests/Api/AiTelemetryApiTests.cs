using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.Api.Middleware;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Common.Telemetry;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// AI telemetry over the wire. The unit tests prove the services build the
/// right record; these prove the record is actually produced by a real HTTP
/// request — that the telemetry logger and the request-context accessor are
/// resolved from the composition root and reach the question-answering
/// pipeline. Without this, the wiring could be absent and every unit test
/// would still pass.
/// </summary>
public class AiTelemetryApiTests : IDisposable
{
    private readonly FakeRetrievalService _retrievalService = new();
    private readonly ScriptedLanguageModelService _languageModel = new();
    private readonly RecordingTelemetryLogger _telemetry = new();
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiTelemetryApiTests()
    {
        _factory = new TestApplicationFactory
        {
            TestServices = services =>
            {
                services.AddSingleton<IRetrievalService>(_retrievalService);
                services.AddSingleton<ILanguageModelService>(_languageModel);

                // Replaces the real logging sink so the emitted records can be
                // inspected. Everything else — the accessor, the middleware,
                // the pipeline wiring — is the production registration.
                services.AddSingleton<IAiRequestTelemetryLogger>(_telemetry);
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

    private async Task<HttpResponseMessage> AskAsync(string question, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/questions")
        {
            Content = JsonContent.Create(new { question }),
        };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task An_Answered_Question_Emits_Telemetry_Stamped_With_The_Requests_Correlation_Id()
    {
        // The whole point of the correlation id here: a reviewer reports "the
        // answer was wrong", quotes the id from the response, and the AI record
        // that produced it can be found. That only works if the id on the record
        // is the id on the response.
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A completed application form is required.","citations":[1]}""");

        var response = await AskAsync("What must an application include?", "telemetry-correlation-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = Assert.Single(_telemetry.Records);
        Assert.Equal("telemetry-correlation-id", record.RequestId);
        Assert.Equal(
            "telemetry-correlation-id",
            Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
        Assert.Equal(TestAuthentication.ReviewerUsername, record.UserId);
        Assert.Equal("What must an application include?", record.Question);
        Assert.Equal("Answered", record.ResponseStatus);
        Assert.NotEmpty(record.RetrievedChunkIds);
    }

    [Fact]
    public async Task A_Question_That_Retrieves_Nothing_Still_Emits_Telemetry()
    {
        _retrievalService.ChunksToReturn = [];

        var response = await AskAsync("What is the airspeed of an unladen swallow?", "no-evidence-correlation-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = Assert.Single(_telemetry.Records);
        Assert.Equal("no-evidence-correlation-id", record.RequestId);
        Assert.Equal("InsufficientEvidence", record.ResponseStatus);
        Assert.Equal(AiTelemetryDefaults.NoModelDeployment, record.ModelDeployment);
    }

    [Fact]
    public async Task Telemetry_Emitted_Over_The_Wire_Carries_No_Retrieved_Text()
    {
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A completed application form is required.","citations":[1]}""");

        await AskAsync("What must an application include?", "redaction-correlation-id");

        var serialized = JsonSerializer.Serialize(Assert.Single(_telemetry.Records));
        Assert.DoesNotContain("A completed application form is required.", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Floodplain Regulations", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_Questions_Emit_Two_Independently_Correlated_Records()
    {
        _retrievalService.ChunksToReturn = [FakeRetrievalService.Chunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A completed application form is required.","citations":[1]}""");
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A completed application form is required.","citations":[1]}""");

        await AskAsync("First question?", "first-correlation-id");
        await AskAsync("Second question?", "second-correlation-id");

        Assert.Equal(2, _telemetry.Records.Count);
        Assert.Equal("first-correlation-id", _telemetry.Records[0].RequestId);
        Assert.Equal("second-correlation-id", _telemetry.Records[1].RequestId);
    }

    /// <summary>Thread-safe recorder; the host may serve requests concurrently.</summary>
    private sealed class RecordingTelemetryLogger : IAiRequestTelemetryLogger
    {
        private readonly List<AiRequestTelemetry> _records = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<AiRequestTelemetry> Records
        {
            get
            {
                lock (_gate)
                {
                    return [.. _records];
                }
            }
        }

        public void LogAiRequest(AiRequestTelemetry telemetry)
        {
            lock (_gate)
            {
                _records.Add(telemetry);
            }
        }
    }
}
