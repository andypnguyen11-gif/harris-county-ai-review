using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Runs the generation harness offline through the real question-answering
/// pipeline and holds the result to the committed baseline.
/// </summary>
/// <remarks>
/// The absolute numbers describe scripted answers over a synthetic corpus, so
/// they are not a measurement of model quality. What they are is a
/// byte-reproducible fingerprint of the whole pipeline: change the grounded
/// prompt, the citation resolver, the fail-closed downgrade, the dataset, or
/// any of the scorers, and this test fails with a reviewable diff.
///
/// Regenerate with <c>UPDATE_EVALUATION_BASELINE=1 dotnet test</c> or
/// <c>evaluation/scripts/run-generation-evaluation.sh --update</c>.
/// </remarks>
public sealed class GenerationEvaluationBaselineTests
{
    private static readonly GenerationEvaluationOptions FixtureOptions = new()
    {
        TopK = 5,
        SupportThreshold = UnsupportedClaimDetector.DefaultSupportThreshold,
        RunType = EvaluationRunType.Fixture,
        PipelineConfiguration = "offline fixture corpus, scripted answers, real Q&A pipeline",
    };

    private static async Task<GenerationEvaluationReport> RunFixtureAsync()
    {
        var pipeline = OfflineGenerationPipeline.Create();
        return await pipeline.Runner.RunAsync(pipeline.Dataset, FixtureOptions);
    }

    [Fact]
    public async Task Fixture_Run_Matches_The_Committed_Baseline()
    {
        var report = await RunFixtureAsync();
        var serialized = EvaluationJson.Serialize(report);

        if (EvaluationWorkspace.ShouldUpdateBaselines)
        {
            EvaluationWorkspace.WriteText(serialized, GenerationEvaluationFiles.FixtureBaseline);
        }

        Assert.True(
            EvaluationWorkspace.Exists(GenerationEvaluationFiles.FixtureBaseline),
            $"No committed generation baseline. Regenerate it with {EvaluationWorkspace.UpdateBaselinesVariable}=1.");

        Assert.Equal(
            EvaluationWorkspace.ReadText(GenerationEvaluationFiles.FixtureBaseline).ReplaceLineEndings("\n"),
            serialized.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task The_Report_Is_Labeled_As_A_Fixture_Run()
    {
        var report = await RunFixtureAsync();

        Assert.Equal(EvaluationRunType.Fixture, report.RunType);
        Assert.All(report.Cases, result => Assert.Null(result.Error));
    }

    [Fact]
    public async Task Every_Answer_Carries_At_Least_One_Citation()
    {
        // The pipeline downgrades an uncitable answer to insufficient evidence,
        // so this rate is a contract, not a score. Anything below 1.0 is a
        // defect in the pipeline rather than a weak run.
        var report = await RunFixtureAsync();

        Assert.Equal(1d, report.Overall.CitationPresenceRate);
        Assert.All(
            report.Cases.Where(result => result.ActualOutcome == QuestionAnswerOutcome.Answered),
            result => Assert.NotEmpty(result.Citations));
    }

    [Fact]
    public async Task Out_Of_Scope_Questions_Are_Declined_Rather_Than_Answered()
    {
        var report = await RunFixtureAsync();

        var outOfScope = report.Cases.Where(result => result.Category == "out-of-scope").ToList();
        Assert.NotEmpty(outOfScope);
        Assert.All(outOfScope, result =>
        {
            Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, result.ActualOutcome);
            Assert.Empty(result.Citations);
        });
        Assert.Equal(1d, report.ByCategory["out-of-scope"].OutcomeMatchRate);
    }

    [Fact]
    public async Task The_Fabricated_Claim_In_The_Fixture_Script_Is_Detected()
    {
        // gen-penalties deliberately asserts a dollar figure the corpus never
        // states. If the detector stops catching it, the metric has quietly
        // stopped meaning anything.
        var report = await RunFixtureAsync();

        var penalties = report.Cases.Single(result => result.Id == "gen-penalties");
        var unsupported = Assert.Single(penalties.UnsupportedClaims);
        Assert.Contains("five thousand dollars", unsupported, StringComparison.OrdinalIgnoreCase);
        Assert.True(report.Overall.UnsupportedClaimRate > 0d);
    }

    [Fact]
    public async Task The_Lexical_Screen_Also_Flags_Faithful_Summary_Sentences()
    {
        // Pinning a known weakness rather than tuning it away. Both of these
        // sentences are faithful to the evidence, but they summarize it in
        // vocabulary the passages never used ("held to the same standards"), so
        // a token-overlap screen cannot tell them from an invention. This is the
        // precision cost of a free, deterministic, always-on check — and the
        // reason the semantic judge exists as a separate step.
        var report = await RunFixtureAsync();

        var flagged = report.Cases
            .Where(result => result.UnsupportedClaims.Count > 0)
            .Select(result => result.Id)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["gen-manufactured-home", "gen-penalties", "gen-substantial-improvement"], flagged);
    }

    [Fact]
    public async Task Sentences_Taken_Straight_From_The_Corpus_Are_Not_Flagged()
    {
        var report = await RunFixtureAsync();

        Assert.True(
            report.Overall.UnsupportedClaimRate < 0.2,
            $"Unsupported-claim rate rose to {report.Overall.UnsupportedClaimRate}; "
            + "the scripted answers have drifted from the fixture corpus.");
    }

    [Fact]
    public async Task Expected_Facts_Are_Covered_By_The_Scripted_Answers()
    {
        // The scripted answers are written from the fixture corpus, so a fact
        // that goes uncovered means the dataset and the fixtures have drifted
        // apart, not that generation regressed.
        var report = await RunFixtureAsync();

        var uncovered = report.Cases
            .SelectMany(result => result.Facts.Where(fact => !fact.IsCovered)
                .Select(fact => $"{result.Id}/{fact.FactId}"))
            .ToList();

        Assert.Empty(uncovered);
        Assert.Equal(1d, report.Overall.MeanFactCoverage);
    }

    [Fact]
    public async Task Cited_Documents_Match_The_Datasets_Expectations()
    {
        var report = await RunFixtureAsync();

        Assert.Equal(1d, report.Overall.CitationTitleAccuracy);
    }

    [Fact]
    public async Task Every_Question_Reaches_The_Model_Exactly_Once_Unless_Retrieval_Found_Nothing()
    {
        var pipeline = OfflineGenerationPipeline.Create();

        var report = await pipeline.Runner.RunAsync(pipeline.Dataset, FixtureOptions);

        var withEvidence = report.Cases.Count(result => result.EvidenceCount > 0);
        Assert.Equal(withEvidence, pipeline.Model.CallCount);
    }

    [Fact]
    public async Task Per_Category_Counts_Add_Up_To_The_Overall_Count()
    {
        var report = await RunFixtureAsync();

        Assert.Equal(report.Overall.QuestionCount, report.Cases.Count);
        Assert.Equal(
            report.Overall.QuestionCount,
            report.ByCategory.Values.Sum(metrics => metrics.QuestionCount));
    }
}
