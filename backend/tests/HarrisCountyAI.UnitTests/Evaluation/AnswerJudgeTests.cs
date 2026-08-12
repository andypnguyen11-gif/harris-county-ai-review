using HarrisCountyAI.Application.Evaluation.Judging;
using HarrisCountyAI.Application.Evaluation.Prompts;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.Common.AI;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The judge fails closed. A judge that quietly invented a middling score when
/// it could not read the response would pull every aggregate toward the middle
/// and hide the regressions it exists to find, so every malformed-response path
/// is pinned here.
/// </summary>
public sealed class AnswerJudgeTests
{
    private const string ValidVerdict = """
        {
          "scores": {"groundedness": 5, "relevance": 4, "completeness": 3, "accuracy": 5, "unsupported_claims": 2},
          "reasoning": {"groundedness": "Traceable.", "unsupported_claims": "One invented figure."},
          "unsupported_claims": ["Civil penalties reach five thousand dollars per day"],
          "summary": "Mostly grounded, one invention."
        }
        """;

    private static JudgeRequest Request(
        string question = "How high must the lowest floor be?",
        string answer = "One foot above the base flood elevation.",
        IReadOnlyList<RetrievedChunk>? evidence = null) => new()
        {
            Question = question,
            Answer = answer,
            Evidence = evidence ?? [FakeRetrievalService.Chunk()],
        };

    [Fact]
    public async Task A_Well_Formed_Verdict_Is_Parsed_In_Criterion_Order()
    {
        var model = new FakeLanguageModelService().EnqueueContent(ValidVerdict);

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(JudgeOutcome.Judged, verdict.Outcome);
        Assert.Equal(Enum.GetValues<JudgeCriterion>(), verdict.Scores.Select(score => score.Criterion));
        Assert.Equal(5, verdict.ScoreFor(JudgeCriterion.Groundedness));
        Assert.Equal(4, verdict.ScoreFor(JudgeCriterion.Relevance));
        Assert.Equal(3, verdict.ScoreFor(JudgeCriterion.Completeness));
        Assert.Equal(5, verdict.ScoreFor(JudgeCriterion.Accuracy));
        Assert.Equal(2, verdict.ScoreFor(JudgeCriterion.UnsupportedClaims));
        Assert.Equal(3.8, verdict.MeanScore);
        Assert.Equal("Mostly grounded, one invention.", verdict.Summary);
        Assert.Equal(JudgePrompt.Version, verdict.PromptVersion);
    }

    [Fact]
    public async Task Unsupported_Claims_Are_Carried_Through_Verbatim()
    {
        var model = new FakeLanguageModelService().EnqueueContent(ValidVerdict);

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(
            ["Civil penalties reach five thousand dollars per day"], verdict.UnsupportedClaims);
    }

    [Fact]
    public async Task A_Criterion_With_No_Stated_Reason_Says_So_Rather_Than_Inventing_One()
    {
        var model = new FakeLanguageModelService().EnqueueContent(ValidVerdict);

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal("Traceable.", verdict.Scores[0].Reasoning);
        Assert.Equal("The judge gave no reason.", verdict.Scores[1].Reasoning);
    }

    [Fact]
    public async Task The_Request_Is_Marked_As_Expecting_Structured_Output()
    {
        var model = new FakeLanguageModelService().EnqueueContent(ValidVerdict);

        await new AnswerJudge(model).JudgeAsync(Request());

        var request = Assert.Single(model.Requests);
        Assert.True(request.ExpectsJsonResponse);
        Assert.Equal(JudgePrompt.ResponseSchemaName, request.JsonResponseSchemaName);
        Assert.Equal(JudgePrompt.Version, request.PromptVersion);
        Assert.Equal(JudgePrompt.SystemPrompt, request.SystemPrompt);
    }

    [Fact]
    public async Task The_Expected_Facts_From_The_Dataset_Reach_The_Prompt()
    {
        var model = new FakeLanguageModelService().EnqueueContent(ValidVerdict);

        await new AnswerJudge(model).JudgeAsync(new JudgeRequest
        {
            Question = "q",
            Answer = "a",
            Evidence = [FakeRetrievalService.Chunk()],
            ExpectedFacts = ["States the one foot freeboard"],
        });

        Assert.Contains(
            "States the one foot freeboard", model.LastRequest!.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Verdict_Wrapped_In_Prose_Or_Code_Fences_Is_Still_Read()
    {
        var model = new FakeLanguageModelService()
            .EnqueueContent($"Here is my assessment:\n```json\n{ValidVerdict}\n```\nHope that helps.");

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(JudgeOutcome.Judged, verdict.Outcome);
    }

    [Fact]
    public async Task Scores_Supplied_As_Strings_Are_Accepted()
    {
        var model = new FakeLanguageModelService().EnqueueContent("""
            {"scores": {"groundedness": "5", "relevance": "5", "completeness": "5",
             "accuracy": "5", "unsupported_claims": "5"}, "summary": "Fine."}
            """);

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(JudgeOutcome.Judged, verdict.Outcome);
        Assert.Equal(5d, verdict.MeanScore);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ this is broken json")]
    [InlineData("""{"summary": "no scores object"}""")]
    [InlineData("""{"scores": "not an object"}""")]
    public async Task An_Unreadable_Response_Yields_Unable_To_Judge(string content)
    {
        var model = new FakeLanguageModelService().EnqueueContent(content);

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(JudgeOutcome.UnableToJudge, verdict.Outcome);
        Assert.Empty(verdict.Scores);
        Assert.Null(verdict.MeanScore);
    }

    [Fact]
    public async Task A_Missing_Criterion_Invalidates_The_Whole_Verdict()
    {
        // A partial verdict would silently change what an aggregate means
        // between runs, so it is refused rather than filled in.
        var model = new FakeLanguageModelService().EnqueueContent("""
            {"scores": {"groundedness": 5, "relevance": 5, "completeness": 5, "accuracy": 5}, "summary": "x"}
            """);

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(JudgeOutcome.UnableToJudge, verdict.Outcome);
        Assert.Contains("unsupported_claims", verdict.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-3)]
    public async Task A_Score_Outside_The_Declared_Scale_Invalidates_The_Verdict(int score)
    {
        var model = new FakeLanguageModelService().EnqueueContent($$"""
            {"scores": {"groundedness": {{score}}, "relevance": 5, "completeness": 5,
             "accuracy": 5, "unsupported_claims": 5}, "summary": "x"}
            """);

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(JudgeOutcome.UnableToJudge, verdict.Outcome);
    }

    [Fact]
    public async Task A_Model_Failure_Yields_Unable_To_Judge_Rather_Than_An_Exception()
    {
        var model = new FakeLanguageModelService()
            .EnqueueException(new InvalidOperationException("endpoint unavailable"));

        var verdict = await new AnswerJudge(model).JudgeAsync(Request());

        Assert.Equal(JudgeOutcome.UnableToJudge, verdict.Outcome);
        Assert.Contains("could not be reached", verdict.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var model = new FakeLanguageModelService { Delay = TimeSpan.FromSeconds(5) };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AnswerJudge(model).JudgeAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task An_Empty_Question_Or_Answer_Is_A_Caller_Bug_Not_A_Low_Score()
    {
        var model = new FakeLanguageModelService();
        var judge = new AnswerJudge(model);

        await Assert.ThrowsAsync<ArgumentException>(() => judge.JudgeAsync(Request(question: "  ")));
        await Assert.ThrowsAsync<ArgumentException>(() => judge.JudgeAsync(Request(answer: "  ")));
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public void A_Judge_Needs_A_Language_Model()
    {
        Assert.Throws<ArgumentNullException>(() => new AnswerJudge(null!));
    }
}
