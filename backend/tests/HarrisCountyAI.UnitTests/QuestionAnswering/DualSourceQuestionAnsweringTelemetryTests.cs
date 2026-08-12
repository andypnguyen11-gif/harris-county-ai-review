using HarrisCountyAI.Application.Common.Telemetry;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.QuestionAnswering.Prompts;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.Common.AI;
using HarrisCountyAI.UnitTests.Common.Telemetry;

namespace HarrisCountyAI.UnitTests.QuestionAnswering;

/// <summary>
/// AI telemetry for the dual-source comparison path. The comparison draws on
/// two corpora at once, so the record has to describe both — and in the same
/// order the model saw them, or a chunk id cannot be matched back to the
/// citation that referenced it.
/// </summary>
public class DualSourceQuestionAnsweringTelemetryTests
{
    private const string Question = "Did the applicant submit everything the county requires?";
    private const string AnsweredJson =
        """{"status":"answered","answer":"The site plan was submitted.","citations":[1,2]}""";

    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-444444444444");

    private readonly FakeRetrievalService _retrieval = new();
    private readonly FakeLanguageModelService _model = new();
    private readonly RecordingAiRequestTelemetryLogger _telemetry = new();
    private readonly StubRequestContextAccessor _requestContext = new()
    {
        CorrelationId = "correlation-xyz",
        UserId = "reviewer@example.gov",
    };

    private readonly DualSourceQuestionAnsweringService _service;

    public DualSourceQuestionAnsweringTelemetryTests()
    {
        _service = new DualSourceQuestionAnsweringService(
            _retrieval, _model, logger: null, telemetryLogger: _telemetry, requestContext: _requestContext);
    }

    private static DualSourceQuestionRequest Request() => new() { Question = Question, CaseId = CaseId };

    private void SeedBothCorpora()
    {
        _retrieval.ChunksByScope[SourceType.County] =
        [
            FakeRetrievalService.Chunk(chunkId: "county-0001", text: "A site plan is required."),
        ];
        _retrieval.ChunksByScope[SourceType.Case] =
        [
            FakeRetrievalService.Chunk(chunkId: "case-0001", text: "Attached: site plan sheet 1 of 2."),
        ];
    }

    [Fact]
    public async Task A_Comparison_Records_Both_Corpora_County_Evidence_First()
    {
        // County block first, then case — the order the prompt presents them in
        // and the order citation numbers resolve against. If this list were
        // reordered, citation N in a stored answer would no longer point at
        // chunk N here, and the audit trail would silently mislead.
        SeedBothCorpora();
        _model.EnqueueContent(AnsweredJson);

        await _service.CompareAsync(Request());

        var record = _telemetry.Single;
        Assert.Equal(["county-0001", "case-0001"], record.RetrievedChunkIds);
        Assert.Equal("correlation-xyz", record.RequestId);
        Assert.Equal(CaseId, record.CaseId);
        Assert.Equal(ComparisonPrompt.Version, record.PromptVersion);
        Assert.Equal(nameof(QuestionAnswerOutcome.Answered), record.ResponseStatus);
        Assert.Equal(10, record.PromptTokens);
        Assert.Equal(5, record.CompletionTokens);
    }

    [Fact]
    public async Task A_One_Sided_Retrieval_Is_Recorded_Without_A_Model_Call()
    {
        // Only county evidence: the comparison fails closed before the model.
        // The record still shows which side was found, which is what explains
        // the non-answer.
        _retrieval.ChunksByScope[SourceType.County] =
        [
            FakeRetrievalService.Chunk(chunkId: "county-0001"),
        ];
        _retrieval.ChunksByScope[SourceType.Case] = [];

        await _service.CompareAsync(Request());

        var record = _telemetry.Single;
        Assert.Equal(nameof(QuestionAnswerOutcome.InsufficientEvidence), record.ResponseStatus);
        Assert.Equal(AiTelemetryDefaults.NoModelDeployment, record.ModelDeployment);
        Assert.Equal(["county-0001"], record.RetrievedChunkIds);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task A_Model_Failure_Is_Recorded_With_Its_Reason()
    {
        SeedBothCorpora();
        _model.EnqueueException(new InvalidOperationException("the deployment is unavailable"));

        await _service.CompareAsync(Request());

        var record = _telemetry.Single;
        Assert.Equal(nameof(QuestionAnswerOutcome.Failed), record.ResponseStatus);
        Assert.Equal("the deployment is unavailable", record.Error);
        Assert.Equal(["county-0001", "case-0001"], record.RetrievedChunkIds);
    }

    [Fact]
    public async Task Telemetry_Never_Carries_County_Or_Case_Document_Text()
    {
        SeedBothCorpora();
        _model.EnqueueContent(AnsweredJson);

        await _service.CompareAsync(Request());

        var serialized = System.Text.Json.JsonSerializer.Serialize(_telemetry.Single);
        Assert.DoesNotContain("A site plan is required.", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Attached: site plan sheet 1 of 2.", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("The site plan was submitted.", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Telemetry_Failure_Does_Not_Fail_The_Comparison()
    {
        SeedBothCorpora();
        _model.EnqueueContent(AnsweredJson);
        _telemetry.ExceptionToThrow = new InvalidOperationException("the telemetry sink is down");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
    }

    [Fact]
    public async Task A_Comparison_With_No_Telemetry_Logger_Still_Answers()
    {
        var service = new DualSourceQuestionAnsweringService(_retrieval, _model);
        SeedBothCorpora();
        _model.EnqueueContent(AnsweredJson);

        var response = await service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
        Assert.Empty(_telemetry.Records);
    }
}
