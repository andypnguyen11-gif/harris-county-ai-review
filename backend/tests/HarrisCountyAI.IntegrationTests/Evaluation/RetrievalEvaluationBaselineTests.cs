using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Retrieval;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Runs the whole retrieval evaluation harness offline against the fixture
/// corpus and holds the result to the committed baseline.
/// </summary>
/// <remarks>
/// The point is regression detection, not benchmarking. The absolute recall
/// figures describe a synthetic corpus and a plain lexical ranker, so they say
/// nothing about production retrieval — but they are byte-reproducible, which
/// means a change to the dataset, the matcher, the metrics, or the runner shows
/// up here as a diff instead of going unnoticed until someone spends money on a
/// live run.
///
/// Regenerate with <c>UPDATE_EVALUATION_BASELINE=1 dotnet test</c> (or
/// <c>evaluation/scripts/run-retrieval-evaluation.sh --fixture --update</c>) and
/// review the diff before committing it.
/// </remarks>
public sealed class RetrievalEvaluationBaselineTests
{
    private static readonly RetrievalEvaluationOptions FixtureOptions = new()
    {
        TopK = 5,
        RecallCutoffs = RetrievalEvaluationOptions.DefaultRecallCutoffs,
        PageTolerance = 1,
        RunType = EvaluationRunType.Fixture,
        RetrievalConfiguration = "offline fixture corpus, BM25-style lexical ranking",
    };

    private static async Task<RetrievalEvaluationReport> RunFixtureAsync()
    {
        var dataset = RetrievalEvaluationDataset.Parse(
            EvaluationWorkspace.ReadText(RetrievalEvaluationFiles.Dataset));
        var runner = new RetrievalEvaluationRunner(FixtureCorpusRetrievalService.FromCommittedCorpus());
        return await runner.RunAsync(dataset, FixtureOptions);
    }

    [Fact]
    public async Task Fixture_Run_Matches_The_Committed_Baseline()
    {
        var report = await RunFixtureAsync();
        var serialized = EvaluationJson.Serialize(report);

        if (EvaluationWorkspace.ShouldUpdateBaselines)
        {
            EvaluationWorkspace.WriteText(serialized, RetrievalEvaluationFiles.FixtureBaseline);
        }

        Assert.True(
            EvaluationWorkspace.Exists(RetrievalEvaluationFiles.FixtureBaseline),
            $"No committed fixture baseline. Regenerate it with {EvaluationWorkspace.UpdateBaselinesVariable}=1.");

        var committed = EvaluationWorkspace.ReadText(RetrievalEvaluationFiles.FixtureBaseline);
        Assert.Equal(
            committed.ReplaceLineEndings("\n"),
            serialized.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Fixture_Run_Is_Labeled_As_A_Fixture_Run()
    {
        // Guards the thing a reader of the results directory most needs to
        // trust: that a committed number says where it came from.
        var report = await RunFixtureAsync();

        Assert.Equal(EvaluationRunType.Fixture, report.RunType);
        Assert.Equal(2, report.DatasetVersion);
    }

    [Fact]
    public async Task Recall_Is_Monotonic_And_Bounded()
    {
        var report = await RunFixtureAsync();

        foreach (var metrics in report.ByCategory.Values.Append(report.Overall))
        {
            Assert.InRange(metrics.RecallAt1!.Value, 0d, 1d);
            Assert.True(metrics.RecallAt1 <= metrics.RecallAt3, "Recall@1 exceeded Recall@3.");
            Assert.True(metrics.RecallAt3 <= metrics.RecallAt5, "Recall@3 exceeded Recall@5.");
            Assert.InRange(metrics.MeanReciprocalRank, 0d, 1d);
        }
    }

    [Fact]
    public async Task Per_Category_Question_Counts_Add_Up_To_The_Overall_Count()
    {
        var report = await RunFixtureAsync();

        Assert.Equal(
            report.Overall.QuestionCount,
            report.ByCategory.Values.Sum(metrics => metrics.QuestionCount));
        Assert.Equal(report.Overall.QuestionCount, report.Cases.Count);
    }

    [Fact]
    public async Task Offline_Retrieval_Finds_The_Expected_Evidence_For_Most_Questions()
    {
        // A floor, not a target. If plain lexical retrieval over the fixture
        // corpus drops below this, either the dataset or the fixture corpus has
        // drifted and the baseline is no longer measuring anything useful.
        var report = await RunFixtureAsync();

        Assert.True(
            report.Overall.RecallAt5 >= 0.5,
            $"Fixture Recall@5 fell to {report.Overall.RecallAt5}; the dataset and fixture corpus have drifted apart.");
    }

    [Fact]
    public async Task Every_Case_Records_The_Evidence_Behind_Its_Verdict()
    {
        var report = await RunFixtureAsync();

        Assert.All(report.Cases, result =>
        {
            Assert.Null(result.Error);
            Assert.Equal(result.RetrievedCount, result.Retrieved.Count);
            Assert.True(result.Retrieved.Count <= FixtureOptions.TopK);
            if (result.FirstMatchRank is { } rank)
            {
                Assert.True(result.Retrieved[rank - 1].IsExpected);
                Assert.All(result.Retrieved.Take(rank - 1), source => Assert.False(source.IsExpected));
            }
            else
            {
                Assert.DoesNotContain(result.Retrieved, source => source.IsExpected);
            }
        });
    }
}
