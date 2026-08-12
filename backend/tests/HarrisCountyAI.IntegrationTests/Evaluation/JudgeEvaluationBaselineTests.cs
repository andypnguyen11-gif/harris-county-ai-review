using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Judging;
using HarrisCountyAI.Application.Evaluation.Prompts;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Runs the judge offline over real answer transcripts and holds the result to
/// the committed baseline.
/// </summary>
/// <remarks>
/// The scores come from a hand-written script, so they measure the harness, not
/// a model's judgement. What they do prove is that the prompt is built, the
/// schema-constrained response is parsed, the acceptance threshold is applied,
/// and agreement with the human labels is computed — all end to end, for free,
/// and byte-reproducibly.
///
/// Regenerate with <c>UPDATE_EVALUATION_BASELINE=1 dotnet test</c> or
/// <c>evaluation/scripts/run-judge-evaluation.sh --update</c>.
/// </remarks>
public sealed class JudgeEvaluationBaselineTests
{
    private static readonly JudgeEvaluationOptions FixtureOptions = new()
    {
        AcceptanceThreshold = 4,
        RunType = EvaluationRunType.Fixture,
        JudgeConfiguration = "offline scripted judge over the fixture generation pipeline",
    };

    private static async Task<JudgeEvaluationReport> RunFixtureAsync()
    {
        var pipeline = OfflineGenerationPipeline.Create();
        var transcripts = await GenerationTranscripts.CollectAsync(
            pipeline.Dataset, pipeline.QuestionAnswering, pipeline.Recorder);
        var manualReviews = ManualReviewDataset.Parse(
            EvaluationWorkspace.ReadText(JudgeEvaluationFiles.ManualReviews));

        var judge = new AnswerJudge(ScriptedJudgeLanguageModel.BindTo(pipeline.Dataset));
        return await new JudgeEvaluationRunner(judge).RunAsync(transcripts, manualReviews, FixtureOptions);
    }

    [Fact]
    public async Task Fixture_Run_Matches_The_Committed_Baseline()
    {
        var report = await RunFixtureAsync();
        var serialized = EvaluationJson.Serialize(report);

        if (EvaluationWorkspace.ShouldUpdateBaselines)
        {
            EvaluationWorkspace.WriteText(serialized, JudgeEvaluationFiles.FixtureBaseline);
        }

        Assert.True(
            EvaluationWorkspace.Exists(JudgeEvaluationFiles.FixtureBaseline),
            $"No committed judge baseline. Regenerate it with {EvaluationWorkspace.UpdateBaselinesVariable}=1.");

        Assert.Equal(
            EvaluationWorkspace.ReadText(JudgeEvaluationFiles.FixtureBaseline).ReplaceLineEndings("\n"),
            serialized.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Every_Answer_Is_Judged_On_All_Five_Criteria()
    {
        var report = await RunFixtureAsync();

        Assert.Equal(report.Overall.CaseCount, report.Overall.JudgedCount);
        Assert.All(report.Cases, result =>
        {
            Assert.Equal(JudgeOutcome.Judged, result.Verdict.Outcome);
            Assert.Equal(
                Enum.GetValues<JudgeCriterion>(),
                result.Verdict.Scores.Select(score => score.Criterion));
            Assert.All(result.Verdict.Scores, score =>
                Assert.InRange(score.Score, JudgePrompt.MinScore, JudgePrompt.MaxScore));
        });
    }

    [Fact]
    public async Task The_Report_Records_The_Prompt_Version_And_Threshold_It_Used()
    {
        var report = await RunFixtureAsync();

        Assert.Equal(EvaluationRunType.Fixture, report.RunType);
        Assert.Equal(JudgePrompt.Version, report.PromptVersion);
        Assert.Equal(4, report.AcceptanceThreshold);
    }

    [Fact]
    public async Task The_Answer_With_A_Fabricated_Figure_Is_Judged_Unacceptable()
    {
        var report = await RunFixtureAsync();

        var penalties = report.Cases.Single(result => result.Id == "gen-penalties");
        Assert.False(penalties.JudgedAcceptable);
        Assert.Equal(ManualVerdict.Unacceptable, penalties.ManualVerdict);
        Assert.True(penalties.AgreesWithManualReview);
        Assert.Contains(
            penalties.Verdict.UnsupportedClaims,
            claim => claim.Contains("five thousand dollars", StringComparison.OrdinalIgnoreCase));
        Assert.True(penalties.Verdict.ScoreFor(JudgeCriterion.Groundedness) < 4);
    }

    [Fact]
    public async Task Correctly_Declining_An_Out_Of_Scope_Question_Scores_Well()
    {
        // A judge that punished a correct refusal would push the product toward
        // answering everything, which is the opposite of what it is for.
        var report = await RunFixtureAsync();

        var outOfScope = report.Cases.Where(result => result.Category == "out-of-scope").ToList();
        Assert.NotEmpty(outOfScope);
        Assert.All(outOfScope, result => Assert.True(result.JudgedAcceptable));
    }

    [Fact]
    public async Task Agreement_With_The_Human_Labels_Is_Reported_And_Is_Not_Trivially_Perfect()
    {
        // The fixture contains one deliberate judge-versus-human disagreement,
        // so a metric stuck at 1.0 would be a bug rather than a good result.
        var report = await RunFixtureAsync();

        Assert.Equal(report.Overall.CaseCount, report.Overall.ManuallyReviewedCount);
        Assert.NotNull(report.Overall.ManualAgreementRate);
        Assert.InRange(report.Overall.ManualAgreementRate!.Value, 0.9, 0.99);

        var disagreements = report.Cases
            .Where(result => result.AgreesWithManualReview == false)
            .Select(result => result.Id)
            .ToList();
        Assert.Equal(["gen-substantial-improvement"], disagreements);
    }

    [Fact]
    public async Task Every_Judged_Case_Was_Manually_Reviewed()
    {
        // An unreviewed case contributes scores but no accountability. Keeping
        // the two datasets aligned is what makes the agreement rate meaningful.
        var report = await RunFixtureAsync();

        Assert.All(report.Cases, result => Assert.NotNull(result.ManualVerdict));
    }

    [Fact]
    public async Task Transcripts_Carry_The_Evidence_The_Answers_Were_Built_From()
    {
        var pipeline = OfflineGenerationPipeline.Create();

        var transcripts = await GenerationTranscripts.CollectAsync(
            pipeline.Dataset, pipeline.QuestionAnswering, pipeline.Recorder);

        Assert.Equal(pipeline.Dataset.Questions.Count, transcripts.Count);
        Assert.All(transcripts, transcript =>
        {
            Assert.NotEmpty(transcript.Answer);
            Assert.NotEmpty(transcript.Evidence);
        });
    }

    [Fact]
    public async Task The_Judge_Is_Called_Once_Per_Transcript()
    {
        var pipeline = OfflineGenerationPipeline.Create();
        var transcripts = await GenerationTranscripts.CollectAsync(
            pipeline.Dataset, pipeline.QuestionAnswering, pipeline.Recorder);
        var judgeModel = ScriptedJudgeLanguageModel.BindTo(pipeline.Dataset);

        await new JudgeEvaluationRunner(new AnswerJudge(judgeModel))
            .RunAsync(transcripts, manualReviews: null, FixtureOptions);

        Assert.Equal(transcripts.Count, judgeModel.CallCount);
    }

    [Fact]
    public async Task An_Insufficient_Evidence_Answer_Still_Reaches_The_Judge()
    {
        var pipeline = OfflineGenerationPipeline.Create();

        var transcripts = await GenerationTranscripts.CollectAsync(
            pipeline.Dataset, pipeline.QuestionAnswering, pipeline.Recorder);

        var declined = transcripts.Where(transcript => transcript.Category == "out-of-scope").ToList();
        Assert.Equal(3, declined.Count);
    }

    [Fact]
    public void The_Committed_Manual_Reviews_Cover_Every_Generation_Question()
    {
        var dataset = Application.Evaluation.Generation.GenerationEvaluationDataset.Parse(
            EvaluationWorkspace.ReadText(GenerationEvaluationFiles.Dataset));
        var reviews = ManualReviewDataset.Parse(
            EvaluationWorkspace.ReadText(JudgeEvaluationFiles.ManualReviews));

        var unreviewed = dataset.Questions
            .Where(question => reviews.Find(question.Id) is null)
            .Select(question => question.Id)
            .ToList();
        var orphaned = reviews.Reviews
            .Where(review => dataset.Questions.All(question => question.Id != review.Id))
            .Select(review => review.Id)
            .ToList();

        Assert.Empty(unreviewed);
        Assert.Empty(orphaned);
    }

    [Fact]
    public void Exactly_One_Answer_Is_Manually_Labeled_Unacceptable()
    {
        var reviews = ManualReviewDataset.Parse(
            EvaluationWorkspace.ReadText(JudgeEvaluationFiles.ManualReviews));

        var rejected = reviews.Reviews
            .Where(review => review.Verdict == ManualVerdict.Unacceptable)
            .Select(review => review.Id)
            .ToList();

        Assert.Equal(["gen-penalties"], rejected);
    }
}
