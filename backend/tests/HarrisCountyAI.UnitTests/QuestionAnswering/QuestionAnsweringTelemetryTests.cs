using HarrisCountyAI.Application.Common.Telemetry;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.QuestionAnswering.Prompts;
using HarrisCountyAI.UnitTests.Common.AI;
using HarrisCountyAI.UnitTests.Common.Telemetry;

namespace HarrisCountyAI.UnitTests.QuestionAnswering;

/// <summary>
/// Covers the AI telemetry emitted by the single-scope question-answering
/// path. The point of these records is that any AI answer can be traced back
/// to the model, prompt, and evidence that produced it, so the assertions are
/// about the record being emitted on <em>every</em> exit path — including the
/// two that never reach the model — and about it never carrying document text.
/// </summary>
public class QuestionAnsweringTelemetryTests
{
    private const string AnsweredJson =
        """{"status":"answered","answer":"A completed application form.","citations":[1]}""";

    private readonly FakeRetrievalService _retrieval = new();
    private readonly FakeLanguageModelService _model = new();
    private readonly RecordingAiRequestTelemetryLogger _telemetry = new();
    private readonly StubRequestContextAccessor _requestContext = new()
    {
        CorrelationId = "correlation-abc",
        UserId = "reviewer@example.gov",
    };

    private readonly QuestionAnsweringService _service;

    public QuestionAnsweringTelemetryTests()
    {
        _service = new QuestionAnsweringService(
            _retrieval, _model, logger: null, telemetryLogger: _telemetry, requestContext: _requestContext);
    }

    private void SeedSources() => _retrieval.ChunksToReturn =
    [
        FakeRetrievalService.Chunk(chunkId: "chunk-a"),
        FakeRetrievalService.Chunk(chunkId: "chunk-b", text: "Two sets of site plans are required."),
    ];

    [Fact]
    public async Task An_Answered_Request_Is_Recorded_With_Its_Model_Prompt_And_Evidence()
    {
        SeedSources();
        _model.EnqueueContent(AnsweredJson);

        await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        var record = _telemetry.Single;
        Assert.Equal("correlation-abc", record.RequestId);
        Assert.Equal("reviewer@example.gov", record.UserId);
        Assert.Equal("What is required?", record.Question);
        Assert.Equal("fake-deployment", record.ModelDeployment);
        Assert.Equal(GroundedQuestionPrompt.Version, record.PromptVersion);
        Assert.Equal(nameof(QuestionAnswerOutcome.Answered), record.ResponseStatus);
        Assert.Equal(["chunk-a", "chunk-b"], record.RetrievedChunkIds);
        Assert.Equal([0.9, 0.9], record.RetrievalScores);
        Assert.Equal(10, record.PromptTokens);
        Assert.Equal(5, record.CompletionTokens);
        Assert.Null(record.Error);
    }

    [Fact]
    public async Task Telemetry_Never_Carries_Retrieved_Or_Answer_Text()
    {
        // The record is allowed to identify evidence; it is not allowed to
        // reproduce it. A telemetry sink is a lower-trust destination than the
        // API response, and case documents must not leak into it.
        SeedSources();
        _model.EnqueueContent(AnsweredJson);

        await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        var serialized = System.Text.Json.JsonSerializer.Serialize(_telemetry.Single);
        Assert.DoesNotContain("A completed application form is required.", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Two sets of site plans are required.", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("A completed application form.", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Request_That_Never_Reaches_The_Model_Is_Still_Recorded()
    {
        // No evidence means no model call, but the request still happened and a
        // reviewer still saw a non-answer. Skipping telemetry here would hide
        // exactly the failures worth investigating: questions the corpus cannot
        // answer at all.
        await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        var record = _telemetry.Single;
        Assert.Equal(nameof(QuestionAnswerOutcome.InsufficientEvidence), record.ResponseStatus);
        Assert.Equal(AiTelemetryDefaults.NoModelDeployment, record.ModelDeployment);
        Assert.Empty(record.RetrievedChunkIds);
        Assert.Null(record.PromptTokens);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task A_Model_Failure_Is_Recorded_With_Its_Reason()
    {
        SeedSources();
        _model.EnqueueException(new InvalidOperationException("the deployment is unavailable"));

        await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        var record = _telemetry.Single;
        Assert.Equal(nameof(QuestionAnswerOutcome.Failed), record.ResponseStatus);
        Assert.Equal("the deployment is unavailable", record.Error);
        Assert.Equal(["chunk-a", "chunk-b"], record.RetrievedChunkIds);
    }

    [Fact]
    public async Task A_Case_Scoped_Request_Records_Its_Case()
    {
        var caseId = Guid.NewGuid();
        SeedSources();
        _model.EnqueueContent(AnsweredJson);

        await _service.AnswerAsync(new QuestionRequest
        {
            Question = "Is the elevation certificate signed?",
            Scope = QuestionScope.Case,
            CaseId = caseId,
        });

        var record = _telemetry.Single;
        Assert.Equal(caseId, record.CaseId);
        Assert.Equal(CaseQuestionPrompt.Version, record.PromptVersion);
    }

    [Fact]
    public async Task A_County_Scoped_Request_Records_No_Case()
    {
        SeedSources();
        _model.EnqueueContent(AnsweredJson);

        await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.Null(_telemetry.Single.CaseId);
    }

    [Fact]
    public async Task Reranking_Scores_Are_Recorded_When_Every_Chunk_Was_Reranked()
    {
        _retrieval.ChunksToReturn =
        [
            FakeRetrievalService.Chunk(chunkId: "chunk-a") with { RerankerScore = 3.5 },
            FakeRetrievalService.Chunk(chunkId: "chunk-b") with { RerankerScore = 2.25 },
        ];
        _model.EnqueueContent(AnsweredJson);

        await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.Equal([3.5, 2.25], _telemetry.Single.RerankingScores);
    }

    [Fact]
    public async Task Reranking_Scores_Are_Omitted_Rather_Than_Padded_When_Only_Some_Chunks_Were_Reranked()
    {
        // The list is positional: index N is the score for chunk N. A partial
        // set cannot honour that without inventing a value, so it reports
        // nothing instead. Padding with 0.0 would read as "ranked last".
        _retrieval.ChunksToReturn =
        [
            FakeRetrievalService.Chunk(chunkId: "chunk-a") with { RerankerScore = 3.5 },
            FakeRetrievalService.Chunk(chunkId: "chunk-b"),
        ];
        _model.EnqueueContent(AnsweredJson);

        await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.Empty(_telemetry.Single.RerankingScores);
        Assert.Equal(2, _telemetry.Single.RetrievedChunkIds.Count);
    }

    [Fact]
    public async Task A_Telemetry_Failure_Does_Not_Fail_The_Answer()
    {
        // Observability is not allowed to cost a reviewer their answer.
        SeedSources();
        _model.EnqueueContent(AnsweredJson);
        _telemetry.ExceptionToThrow = new InvalidOperationException("the telemetry sink is down");

        var response = await _service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
        Assert.NotEmpty(response.Citations);
    }

    [Fact]
    public async Task A_Call_With_No_Ambient_Request_Is_Recorded_With_A_Placeholder_Id()
    {
        // The offline evaluation harness drives this service with no HTTP
        // request at all. That must still produce a record, and the id must be
        // unmistakably not a real correlation id.
        var service = new QuestionAnsweringService(
            _retrieval, _model, logger: null, telemetryLogger: _telemetry, requestContext: null);
        SeedSources();
        _model.EnqueueContent(AnsweredJson);

        await service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        var record = _telemetry.Single;
        Assert.Equal(AiTelemetryDefaults.NoRequestId, record.RequestId);
        Assert.Null(record.UserId);
    }

    [Fact]
    public async Task A_Service_With_No_Telemetry_Logger_Still_Answers()
    {
        var service = new QuestionAnsweringService(_retrieval, _model);
        SeedSources();
        _model.EnqueueContent(AnsweredJson);

        var response = await service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
        Assert.Empty(_telemetry.Records);
    }

    [Fact]
    public async Task Exactly_One_Record_Is_Emitted_Per_Request()
    {
        SeedSources();
        _model.EnqueueContent(AnsweredJson);
        _model.EnqueueContent(AnsweredJson);

        await _service.AnswerAsync(new QuestionRequest { Question = "First question?" });
        await _service.AnswerAsync(new QuestionRequest { Question = "Second question?" });

        Assert.Equal(2, _telemetry.Records.Count);
        Assert.Equal("First question?", _telemetry.Records[0].Question);
        Assert.Equal("Second question?", _telemetry.Records[1].Question);
    }

    [Fact]
    public async Task A_Rejected_Request_Emits_No_Record()
    {
        // Argument validation rejects the call before any AI work happens, so
        // there is no AI request to describe.
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AnswerAsync(new QuestionRequest { Question = "   " }));

        Assert.Empty(_telemetry.Records);
    }
}
