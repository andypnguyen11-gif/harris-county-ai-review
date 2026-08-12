using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Judging;
using HarrisCountyAI.Application.Evaluation.Prompts;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The runner turns verdicts into the judge report, and — more importantly —
/// into the agreement rate against human labels, which is the only thing that
/// says whether the judge is worth listening to.
/// </summary>
public sealed class JudgeEvaluationRunnerTests
{
    private sealed class StubJudge : IAnswerJudge
    {
        private readonly Func<JudgeRequest, JudgeVerdict> _factory;

        public StubJudge(Func<JudgeRequest, JudgeVerdict> factory) => _factory = factory;

        public List<JudgeRequest> Requests { get; } = [];

        public Task<JudgeVerdict> JudgeAsync(
            JudgeRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(_factory(request));
        }
    }

    private static JudgeVerdict Verdict(int score, params string[] unsupportedClaims) => new()
    {
        Outcome = JudgeOutcome.Judged,
        Scores = [.. Enum.GetValues<JudgeCriterion>().Select(criterion => new JudgeCriterionScore
        {
            Criterion = criterion,
            Score = score,
            Reasoning = "because",
        })],
        UnsupportedClaims = unsupportedClaims,
        Summary = "summary",
        PromptVersion = JudgePrompt.Version,
        ModelDeployment = "stub",
    };

    private static JudgeVerdict Unjudgeable() => new()
    {
        Outcome = JudgeOutcome.UnableToJudge,
        Scores = [],
        UnsupportedClaims = [],
        Summary = "could not judge",
        PromptVersion = JudgePrompt.Version,
    };

    private static JudgeEvaluationInput Input(
        string id = "gen-01", string category = "answerable", string answer = "an answer") => new()
        {
            Id = id,
            Category = category,
            Question = $"question for {id}",
            Answer = answer,
            Evidence = [FakeRetrievalService.Chunk()],
        };

    private static ManualReviewDataset Reviews(params (string Id, ManualVerdict Verdict)[] reviews) => new()
    {
        Reviews = [.. reviews.Select(review => new ManualReview
        {
            Id = review.Id,
            Verdict = review.Verdict,
            Notes = "reviewed",
        })],
    };

    [Fact]
    public async Task Each_Transcript_Is_Judged_With_Its_Own_Evidence_And_Expectations()
    {
        var judge = new StubJudge(_ => Verdict(5));

        await new JudgeEvaluationRunner(judge).RunAsync(
        [
            Input("gen-01") with { ExpectedFacts = ["states the freeboard"] },
            Input("gen-02"),
        ]);

        Assert.Equal(2, judge.Requests.Count);
        Assert.Equal("question for gen-01", judge.Requests[0].Question);
        Assert.Equal(["states the freeboard"], judge.Requests[0].ExpectedFacts);
        Assert.Single(judge.Requests[0].Evidence);
    }

    [Fact]
    public async Task A_Case_Is_Acceptable_Only_When_Every_Criterion_Clears_The_Threshold()
    {
        // One weak criterion is enough to sink an answer: a perfectly relevant,
        // complete answer that invents a fact is still not acceptable.
        var judge = new StubJudge(request => request.Question.Contains("gen-01", StringComparison.Ordinal)
            ? Verdict(5) with
            {
                Scores =
                [
                    new JudgeCriterionScore
                    {
                        Criterion = JudgeCriterion.Groundedness, Score = 2, Reasoning = "invented",
                    },
                    .. Enum.GetValues<JudgeCriterion>().Skip(1).Select(criterion => new JudgeCriterionScore
                    {
                        Criterion = criterion, Score = 5, Reasoning = "fine",
                    }),
                ],
            }
            : Verdict(5));

        var report = await new JudgeEvaluationRunner(judge).RunAsync([Input("gen-01"), Input("gen-02")]);

        Assert.False(report.Cases[0].JudgedAcceptable);
        Assert.True(report.Cases[1].JudgedAcceptable);
        Assert.Equal(0.5, report.Overall.AcceptableRate);
    }

    [Fact]
    public async Task The_Threshold_Is_Configurable()
    {
        var judge = new StubJudge(_ => Verdict(3));

        var strict = await new JudgeEvaluationRunner(judge).RunAsync(
            [Input()], options: new JudgeEvaluationOptions { AcceptanceThreshold = 4 });
        var lenient = await new JudgeEvaluationRunner(judge).RunAsync(
            [Input()], options: new JudgeEvaluationOptions { AcceptanceThreshold = 3 });

        Assert.False(Assert.Single(strict.Cases).JudgedAcceptable);
        Assert.True(Assert.Single(lenient.Cases).JudgedAcceptable);
    }

    [Fact]
    public async Task Agreement_With_A_Human_Label_Is_Computed_Both_Ways()
    {
        var judge = new StubJudge(request =>
            request.Question.Contains("gen-01", StringComparison.Ordinal) ? Verdict(5) : Verdict(2));

        var report = await new JudgeEvaluationRunner(judge).RunAsync(
            [Input("gen-01"), Input("gen-02"), Input("gen-03")],
            Reviews(
                ("gen-01", ManualVerdict.Acceptable),
                ("gen-02", ManualVerdict.Unacceptable),
                ("gen-03", ManualVerdict.Acceptable)));

        // The judge agrees on the first two and is harsher than the human on the third.
        Assert.True(report.Cases[0].AgreesWithManualReview);
        Assert.True(report.Cases[1].AgreesWithManualReview);
        Assert.False(report.Cases[2].AgreesWithManualReview);
        Assert.Equal(3, report.Overall.ManuallyReviewedCount);
        Assert.Equal(0.6667, report.Overall.ManualAgreementRate);
    }

    [Fact]
    public async Task An_Unreviewed_Case_Is_Excluded_From_Agreement_Rather_Than_Counted_As_Agreement()
    {
        var judge = new StubJudge(_ => Verdict(5));

        var report = await new JudgeEvaluationRunner(judge).RunAsync(
            [Input("gen-01"), Input("gen-02")],
            Reviews(("gen-01", ManualVerdict.Acceptable)));

        Assert.Null(report.Cases[1].AgreesWithManualReview);
        Assert.Null(report.Cases[1].ManualVerdict);
        Assert.Equal(1, report.Overall.ManuallyReviewedCount);
        Assert.Equal(1d, report.Overall.ManualAgreementRate);
    }

    [Fact]
    public async Task With_No_Human_Labels_At_All_The_Agreement_Rate_Is_Null()
    {
        var report = await new JudgeEvaluationRunner(new StubJudge(_ => Verdict(5)))
            .RunAsync([Input()]);

        Assert.Null(report.Overall.ManualAgreementRate);
        Assert.Equal(0, report.Overall.ManuallyReviewedCount);
    }

    [Fact]
    public async Task A_Case_The_Judge_Could_Not_Score_Is_Excluded_Rather_Than_Counted_As_Bad()
    {
        // The judge failing says nothing about the answer, so it must not drag
        // the aggregate down as if the answer were poor.
        var judge = new StubJudge(request =>
            request.Question.Contains("gen-01", StringComparison.Ordinal) ? Unjudgeable() : Verdict(5));

        var report = await new JudgeEvaluationRunner(judge).RunAsync(
            [Input("gen-01"), Input("gen-02")],
            Reviews(("gen-01", ManualVerdict.Acceptable), ("gen-02", ManualVerdict.Acceptable)));

        Assert.Equal(2, report.Overall.CaseCount);
        Assert.Equal(1, report.Overall.JudgedCount);
        Assert.Null(report.Cases[0].JudgedAcceptable);
        Assert.Null(report.Cases[0].AgreesWithManualReview);
        Assert.Equal(1d, report.Overall.AcceptableRate);
        Assert.Equal(1, report.Overall.ManuallyReviewedCount);
    }

    [Fact]
    public async Task Mean_Scores_Are_Reported_Per_Criterion()
    {
        var judge = new StubJudge(request =>
            request.Question.Contains("gen-01", StringComparison.Ordinal) ? Verdict(5) : Verdict(3));

        var report = await new JudgeEvaluationRunner(judge).RunAsync([Input("gen-01"), Input("gen-02")]);

        Assert.Equal(4d, report.Overall.MeanScore);
        Assert.All(
            Enum.GetValues<JudgeCriterion>(),
            criterion => Assert.Equal(4d, report.Overall.MeanScoreByCriterion[criterion.ToString()]));
    }

    [Fact]
    public async Task Unsupported_Claims_Are_Counted_Across_The_Run()
    {
        var judge = new StubJudge(request =>
            request.Question.Contains("gen-01", StringComparison.Ordinal)
                ? Verdict(2, "invented one", "invented two")
                : Verdict(5));

        var report = await new JudgeEvaluationRunner(judge).RunAsync([Input("gen-01"), Input("gen-02")]);

        Assert.Equal(2, report.Overall.UnsupportedClaimCount);
    }

    [Fact]
    public async Task Metrics_Are_Broken_Down_By_Category()
    {
        var judge = new StubJudge(_ => Verdict(5));

        var report = await new JudgeEvaluationRunner(judge).RunAsync(
            [Input("a1"), Input("a2"), Input("o1", "out-of-scope")]);

        Assert.Equal(
            ["answerable", "out-of-scope"], report.ByCategory.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, report.ByCategory["answerable"].CaseCount);
        Assert.Equal(1, report.ByCategory["out-of-scope"].CaseCount);
    }

    [Fact]
    public async Task The_Report_Records_How_The_Run_Was_Configured()
    {
        var report = await new JudgeEvaluationRunner(new StubJudge(_ => Verdict(5))).RunAsync(
            [Input()],
            manualReviews: null,
            new JudgeEvaluationOptions
            {
                AcceptanceThreshold = 5,
                RunType = EvaluationRunType.Live,
                JudgeConfiguration = "live gpt judge",
            });

        Assert.Equal(EvaluationRunType.Live, report.RunType);
        Assert.Equal(5, report.AcceptanceThreshold);
        Assert.Equal("live gpt judge", report.JudgeConfiguration);
        Assert.Equal(JudgePrompt.Version, report.PromptVersion);
    }

    [Fact]
    public async Task A_Threshold_Outside_The_Scale_Is_Rejected()
    {
        var judge = new StubJudge(_ => Verdict(5));

        await Assert.ThrowsAsync<ArgumentException>(() => new JudgeEvaluationRunner(judge).RunAsync(
            [Input()], manualReviews: null, new JudgeEvaluationOptions { AcceptanceThreshold = 9 }));
        Assert.Empty(judge.Requests);
    }

    [Fact]
    public async Task An_Empty_Run_Is_Rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new JudgeEvaluationRunner(new StubJudge(_ => Verdict(5))).RunAsync([]));
    }

    [Fact]
    public async Task Cancellation_Stops_The_Run()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new JudgeEvaluationRunner(new StubJudge(_ => Verdict(5)))
                .RunAsync([Input()], manualReviews: null, options: null, cancellation.Token));
    }

    [Fact]
    public void A_Runner_Needs_A_Judge()
    {
        Assert.Throws<ArgumentNullException>(() => new JudgeEvaluationRunner(null!));
    }
}
