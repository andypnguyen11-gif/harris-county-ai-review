using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Retrieval;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The runner turns retrieval behaviour into the report the project reasons
/// about. These tests cover the contract that makes the report trustworthy:
/// corpus scope only, deterministic scoring, per-category breakdown, and a
/// failing question that degrades to a miss instead of killing the run.
/// </summary>
public sealed class RetrievalEvaluationRunnerTests
{
    private static readonly ExpectedSource Regulations = new() { Title = "Floodplain Regulations" };

    private static RetrievalEvaluationDataset Dataset(params RetrievalEvaluationCase[] cases) =>
        new() { Version = 2, Questions = cases };

    private static RetrievalEvaluationCase Case(
        string id = "q1",
        string category = "semantic",
        string question = "How high must the lowest floor be?",
        params ExpectedSource[] expected) =>
        new()
        {
            Id = id,
            Category = category,
            Question = question,
            ExpectedSources = expected.Length == 0 ? [Regulations] : expected,
        };

    [Fact]
    public async Task Retrieval_Is_Scoped_To_The_County_Corpus_With_The_Requested_Depth()
    {
        var retrieval = new FakeRetrievalService();
        var runner = new RetrievalEvaluationRunner(retrieval);

        await runner.RunAsync(Dataset(Case()), new RetrievalEvaluationOptions { TopK = 5 });

        var request = Assert.Single(retrieval.Requests);
        Assert.Equal(SourceType.County, request.Scope);
        Assert.Null(request.CaseId);
        Assert.Equal(5, request.TopK);
        Assert.Equal("How high must the lowest floor be?", request.Query);
    }

    [Fact]
    public async Task The_First_Matching_Chunk_Sets_The_Rank()
    {
        var retrieval = new FakeRetrievalService
        {
            ChunksToReturn =
            [
                FakeRetrievalService.Chunk(title: "Drainage Manual", section: null, page: null),
                FakeRetrievalService.Chunk(title: "Floodplain Regulations", section: null, page: null),
                FakeRetrievalService.Chunk(title: "Floodplain Regulations", section: null, page: null),
            ],
        };
        var report = await new RetrievalEvaluationRunner(retrieval).RunAsync(Dataset(Case()));

        var result = Assert.Single(report.Cases);
        Assert.Equal(2, result.FirstMatchRank);
        Assert.Equal(3, result.RetrievedCount);
        Assert.Equal([false, true, true], result.Retrieved.Select(source => source.IsExpected));
    }

    [Fact]
    public async Task A_Question_Whose_Evidence_Never_Appears_Is_A_Miss_Not_An_Error()
    {
        var retrieval = new FakeRetrievalService
        {
            ChunksToReturn = [FakeRetrievalService.Chunk(title: "Drainage Manual", section: null, page: null)],
        };
        var report = await new RetrievalEvaluationRunner(retrieval).RunAsync(Dataset(Case()));

        var result = Assert.Single(report.Cases);
        Assert.Null(result.FirstMatchRank);
        Assert.Null(result.Error);
        Assert.Equal(0d, report.Overall.RecallAt5);
    }

    [Fact]
    public async Task A_Retrieval_Failure_Is_Recorded_And_Scored_As_A_Miss()
    {
        // One broken query should not cost the whole run: the report still
        // covers every question, and the failure is preserved for diagnosis.
        var retrieval = new FakeRetrievalService
        {
            ExceptionToThrow = new InvalidOperationException("search unavailable"),
        };
        var report = await new RetrievalEvaluationRunner(retrieval).RunAsync(
            Dataset(Case("q1"), Case("q2")));

        Assert.Equal(2, report.Cases.Count);
        Assert.All(report.Cases, result =>
        {
            Assert.Equal("search unavailable", result.Error);
            Assert.Null(result.FirstMatchRank);
            Assert.Equal(0, result.RetrievedCount);
            Assert.Empty(result.Retrieved);
        });
        Assert.Equal(0d, report.Overall.RecallAt5);
    }

    [Fact]
    public async Task Metrics_Are_Broken_Down_By_Category()
    {
        var retrieval = new FakeRetrievalService();
        retrieval.ChunksToReturn = [FakeRetrievalService.Chunk(title: "Floodplain Regulations", section: null, page: null)];
        var runner = new RetrievalEvaluationRunner(retrieval);

        var report = await runner.RunAsync(Dataset(
            Case("s1", "section-number"),
            Case("m1", "semantic"),
            Case("m2", "semantic")));

        Assert.Equal(["section-number", "semantic"], report.ByCategory.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1, report.ByCategory["section-number"].QuestionCount);
        Assert.Equal(2, report.ByCategory["semantic"].QuestionCount);
        Assert.Equal(3, report.Overall.QuestionCount);
    }

    [Fact]
    public async Task The_Report_Records_How_The_Run_Was_Configured()
    {
        var report = await new RetrievalEvaluationRunner(new FakeRetrievalService()).RunAsync(
            Dataset(Case()),
            new RetrievalEvaluationOptions
            {
                TopK = 3,
                RecallCutoffs = [1, 3],
                PageTolerance = 0,
                RunType = EvaluationRunType.Live,
                RetrievalConfiguration = "hybrid + reranking",
            });

        Assert.Equal(EvaluationRunType.Live, report.RunType);
        Assert.Equal(3, report.TopK);
        Assert.Equal(0, report.PageTolerance);
        Assert.Equal("hybrid + reranking", report.RetrievalConfiguration);
        Assert.Equal(2, report.DatasetVersion);
        Assert.Equal([1, 3], report.Overall.RecallAtK.Keys.Order());
    }

    [Fact]
    public async Task Page_Tolerance_From_The_Options_Reaches_The_Matcher()
    {
        var retrieval = new FakeRetrievalService
        {
            ChunksToReturn = [FakeRetrievalService.Chunk(title: "Floodplain Regulations", section: null, page: 18)],
        };
        var dataset = Dataset(Case(
            "page-01",
            "section-number",
            "What is on page 17?",
            new ExpectedSource { Title = "Floodplain Regulations", Page = 17 }));

        var lenient = await new RetrievalEvaluationRunner(retrieval).RunAsync(
            dataset, new RetrievalEvaluationOptions { PageTolerance = 1 });
        var strict = await new RetrievalEvaluationRunner(retrieval).RunAsync(
            dataset, new RetrievalEvaluationOptions { PageTolerance = 0 });

        Assert.Equal(1, Assert.Single(lenient.Cases).FirstMatchRank);
        Assert.Null(Assert.Single(strict.Cases).FirstMatchRank);
    }

    [Fact]
    public async Task A_Cutoff_Deeper_Than_The_Retrieved_Depth_Is_Rejected()
    {
        var runner = new RetrievalEvaluationRunner(new FakeRetrievalService());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(
            Dataset(Case()), new RetrievalEvaluationOptions { TopK = 3, RecallCutoffs = [1, 3, 5] }));

        Assert.Contains("Recall@5 cannot be measured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Empty_Dataset_Is_Rejected_Before_Any_Retrieval_Runs()
    {
        var retrieval = new FakeRetrievalService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RetrievalEvaluationRunner(retrieval).RunAsync(new RetrievalEvaluationDataset { Questions = [] }));

        Assert.Empty(retrieval.Requests);
    }

    [Fact]
    public async Task Cancellation_Stops_The_Run()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RetrievalEvaluationRunner(new FakeRetrievalService())
                .RunAsync(Dataset(Case()), options: null, cancellation.Token));
    }

    [Fact]
    public void A_Runner_Needs_A_Retrieval_Service()
    {
        Assert.Throws<ArgumentNullException>(() => new RetrievalEvaluationRunner(null!));
    }
}
