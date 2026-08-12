using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The runner turns pipeline behaviour into the generation report. These tests
/// cover the contract behind every number in it.
/// </summary>
public sealed class GenerationEvaluationRunnerTests
{
    private static GenerationEvaluationDataset Dataset(params GenerationEvaluationCase[] cases) =>
        new() { Version = 1, Questions = cases };

    private static GenerationEvaluationCase Answerable(
        string id = "q1",
        string category = "answerable",
        string question = "How high must the lowest floor be?",
        IReadOnlyList<ExpectedFact>? facts = null,
        IReadOnlyList<string>? citationTitles = null) => new()
        {
            Id = id,
            Category = category,
            Question = question,
            ExpectedOutcome = QuestionAnswerOutcome.Answered,
            ExpectedFacts = facts ??
            [
                new ExpectedFact
                {
                    Id = "freeboard",
                    Description = "One foot above the base flood elevation",
                    RequiredPhrases = ["one foot"],
                },
            ],
            ExpectedCitationTitles = citationTitles ?? [],
        };

    private static GenerationEvaluationCase OutOfScope(string id = "oos1") => new()
    {
        Id = id,
        Category = "out-of-scope",
        Question = "What is the property tax rate?",
        ExpectedOutcome = QuestionAnswerOutcome.InsufficientEvidence,
    };

    private sealed class StubQuestionAnswering : IQuestionAnsweringService
    {
        public List<QuestionRequest> Requests { get; } = [];

        public QuestionResponse Response { get; set; } = new()
        {
            Outcome = QuestionAnswerOutcome.Answered,
            Answer = "The lowest floor must be one foot above the base flood elevation.",
            Citations = [],
            PromptVersion = "corpus-qa/v1",
            ModelDeployment = "stub",
        };

        public Exception? ExceptionToThrow { get; set; }

        public Task<QuestionResponse> AnswerAsync(
            QuestionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ExceptionToThrow is not null
                ? throw ExceptionToThrow
                : Task.FromResult(Response);
        }
    }

    private sealed class StubRecorder : IEvaluationEvidenceRecorder
    {
        public IReadOnlyList<RetrievedChunk> Evidence { get; set; } = [];

        public int DrainCount { get; private set; }

        public IReadOnlyList<RetrievedChunk> Drain()
        {
            DrainCount++;
            return Evidence;
        }
    }

    private static Citation Cite(string title, int number = 1) => new()
    {
        Number = number,
        Source = SourceType.County,
        ChunkId = $"chunk-{number}",
        DocumentId = Guid.Parse("0f8fad5b-d9cb-469f-a165-408319b0e0d9"),
        Title = title,
    };

    [Fact]
    public async Task Questions_Are_Asked_Of_The_County_Corpus_At_The_Configured_Depth()
    {
        var pipeline = new StubQuestionAnswering();

        await new GenerationEvaluationRunner(pipeline).RunAsync(
            Dataset(Answerable()), new GenerationEvaluationOptions { TopK = 3 });

        var request = Assert.Single(pipeline.Requests);
        Assert.Equal(QuestionScope.County, request.Scope);
        Assert.Null(request.CaseId);
        Assert.Equal(3, request.TopK);
    }

    [Fact]
    public async Task An_Outcome_Matching_The_Expectation_Is_Recorded_As_A_Match()
    {
        var pipeline = new StubQuestionAnswering
        {
            Response = new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                Answer = "The corpus does not cover property tax.",
                Citations = [],
                PromptVersion = "corpus-qa/v1",
            },
        };

        var report = await new GenerationEvaluationRunner(pipeline).RunAsync(Dataset(OutOfScope()));

        var result = Assert.Single(report.Cases);
        Assert.True(result.OutcomeMatched);
        Assert.Equal(1d, report.Overall.OutcomeMatchRate);
    }

    [Fact]
    public async Task An_Answer_To_An_Out_Of_Scope_Question_Is_Scored_As_A_Mismatch()
    {
        // The failure this product most needs to catch: a fluent answer to a
        // question the corpus cannot support.
        var pipeline = new StubQuestionAnswering
        {
            Response = new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.Answered,
                Answer = "The residential rate is confidently stated here.",
                Citations = [Cite("Floodplain Regulations")],
                PromptVersion = "corpus-qa/v1",
            },
        };

        var report = await new GenerationEvaluationRunner(pipeline).RunAsync(Dataset(OutOfScope()));

        Assert.False(Assert.Single(report.Cases).OutcomeMatched);
        Assert.Equal(0d, report.Overall.OutcomeMatchRate);
    }

    [Fact]
    public async Task Fact_Coverage_Is_Reported_Per_Question_And_Averaged()
    {
        var pipeline = new StubQuestionAnswering();
        var facts = new ExpectedFact[]
        {
            new() { Id = "covered", Description = "d", RequiredPhrases = ["one foot"] },
            new() { Id = "missing", Description = "d", RequiredPhrases = ["two feet"] },
        };

        var report = await new GenerationEvaluationRunner(pipeline).RunAsync(
            Dataset(Answerable(facts: facts)));

        var result = Assert.Single(report.Cases);
        Assert.Equal(0.5, result.FactCoverage);
        Assert.Equal(0.5, report.Overall.MeanFactCoverage);
        Assert.Equal(0d, report.Overall.FullFactCoverageRate);
    }

    [Fact]
    public async Task An_Unanswered_Question_Covers_No_Facts_Without_Scoring_Its_Refusal_Text()
    {
        // Scoring the "not enough evidence" prose for coverage would penalize
        // the pipeline for behaving correctly, so facts are marked uncovered
        // without being matched against it.
        var pipeline = new StubQuestionAnswering
        {
            Response = new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                Answer = "One foot of evidence is missing.",
                Citations = [],
                PromptVersion = "corpus-qa/v1",
            },
        };

        var report = await new GenerationEvaluationRunner(pipeline).RunAsync(Dataset(Answerable()));

        var result = Assert.Single(report.Cases);
        Assert.Equal(0d, result.FactCoverage);
        Assert.All(result.Facts, fact => Assert.False(fact.IsCovered));
    }

    [Fact]
    public async Task Citation_Titles_Are_Scored_Only_When_The_Dataset_Records_Them()
    {
        var pipeline = new StubQuestionAnswering
        {
            Response = new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.Answered,
                Answer = "The lowest floor must be one foot above the base flood elevation.",
                Citations = [Cite("Floodplain Regulations")],
                PromptVersion = "corpus-qa/v1",
            },
        };

        var scored = await new GenerationEvaluationRunner(pipeline).RunAsync(
            Dataset(Answerable(citationTitles: ["Floodplain Regulations"])));
        var unscored = await new GenerationEvaluationRunner(pipeline).RunAsync(Dataset(Answerable()));

        Assert.True(Assert.Single(scored.Cases).CitationTitlesMatched);
        Assert.Null(Assert.Single(unscored.Cases).CitationTitlesMatched);
        Assert.Null(unscored.Overall.CitationTitleAccuracy);
    }

    [Fact]
    public async Task Citing_A_Document_The_Dataset_Did_Not_Expect_Fails_The_Check()
    {
        var pipeline = new StubQuestionAnswering
        {
            Response = new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.Answered,
                Answer = "The lowest floor must be one foot above the base flood elevation.",
                Citations = [Cite("Floodplain Regulations"), Cite("Fee Schedule", 2)],
                PromptVersion = "corpus-qa/v1",
            },
        };

        var report = await new GenerationEvaluationRunner(pipeline).RunAsync(
            Dataset(Answerable(citationTitles: ["Floodplain Regulations"])));

        Assert.False(Assert.Single(report.Cases).CitationTitlesMatched);
        Assert.Equal(0d, report.Overall.CitationTitleAccuracy);
    }

    [Fact]
    public async Task Claim_Analysis_Uses_The_Evidence_The_Pipeline_Actually_Retrieved()
    {
        var recorder = new StubRecorder
        {
            Evidence = [FakeRetrievalService.Chunk(
                text: "The lowest floor shall be elevated one foot above the base flood elevation.",
                section: null,
                page: null)],
        };
        var pipeline = new StubQuestionAnswering();

        var report = await new GenerationEvaluationRunner(pipeline, recorder).RunAsync(Dataset(Answerable()));

        var result = Assert.Single(report.Cases);
        Assert.NotNull(result.Claims);
        Assert.Empty(result.UnsupportedClaims);
        Assert.Equal(1, result.EvidenceCount);
        Assert.Equal(0d, report.Overall.UnsupportedClaimRate);
    }

    [Fact]
    public async Task Without_A_Recorder_Claim_Metrics_Are_Null_Rather_Than_Guessed()
    {
        var report = await new GenerationEvaluationRunner(new StubQuestionAnswering())
            .RunAsync(Dataset(Answerable()));

        var result = Assert.Single(report.Cases);
        Assert.Null(result.Claims);
        Assert.Empty(result.UnsupportedClaims);
        Assert.Null(report.Overall.UnsupportedClaimRate);
        Assert.Null(report.Overall.AnswersWithUnsupportedClaimsRate);
    }

    [Fact]
    public async Task Evidence_Is_Drained_Before_Each_Question_So_It_Cannot_Leak_Between_Cases()
    {
        var recorder = new StubRecorder();

        await new GenerationEvaluationRunner(new StubQuestionAnswering(), recorder)
            .RunAsync(Dataset(Answerable("q1"), Answerable("q2")));

        // Once before and once after each of the two questions.
        Assert.Equal(4, recorder.DrainCount);
    }

    [Fact]
    public async Task A_Pipeline_Exception_Is_Recorded_And_Scored_As_A_Failure()
    {
        var pipeline = new StubQuestionAnswering
        {
            ExceptionToThrow = new InvalidOperationException("model unavailable"),
        };

        var report = await new GenerationEvaluationRunner(pipeline).RunAsync(Dataset(Answerable()));

        var result = Assert.Single(report.Cases);
        Assert.Equal("model unavailable", result.Error);
        Assert.Equal(QuestionAnswerOutcome.Failed, result.ActualOutcome);
        Assert.False(result.OutcomeMatched);
        Assert.Equal(0d, result.FactCoverage);
    }

    [Fact]
    public async Task The_Report_Records_How_The_Run_Was_Configured()
    {
        var report = await new GenerationEvaluationRunner(new StubQuestionAnswering()).RunAsync(
            Dataset(Answerable()),
            new GenerationEvaluationOptions
            {
                TopK = 4,
                SupportThreshold = 0.8,
                RunType = EvaluationRunType.Live,
                PipelineConfiguration = "live hybrid + gpt",
            });

        Assert.Equal(EvaluationRunType.Live, report.RunType);
        Assert.Equal(4, report.TopK);
        Assert.Equal(0.8, report.SupportThreshold);
        Assert.Equal("live hybrid + gpt", report.PipelineConfiguration);
        Assert.Equal(1, report.DatasetVersion);
    }

    [Fact]
    public async Task Metrics_Are_Broken_Down_By_Category()
    {
        var report = await new GenerationEvaluationRunner(new StubQuestionAnswering()).RunAsync(
            Dataset(Answerable("a1"), Answerable("a2"), OutOfScope()));

        Assert.Equal(
            ["answerable", "out-of-scope"], report.ByCategory.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, report.ByCategory["answerable"].QuestionCount);
        Assert.Equal(1, report.ByCategory["out-of-scope"].QuestionCount);
    }

    [Fact]
    public async Task An_Empty_Dataset_Is_Rejected_Before_Anything_Is_Asked()
    {
        var pipeline = new StubQuestionAnswering();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GenerationEvaluationRunner(pipeline).RunAsync(
                new GenerationEvaluationDataset { Questions = [] }));

        Assert.Empty(pipeline.Requests);
    }

    [Fact]
    public async Task Cancellation_Stops_The_Run()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GenerationEvaluationRunner(new StubQuestionAnswering())
                .RunAsync(Dataset(Answerable()), options: null, cancellation.Token));
    }

    [Fact]
    public void A_Runner_Needs_A_Question_Answering_Service()
    {
        Assert.Throws<ArgumentNullException>(() => new GenerationEvaluationRunner(null!));
    }
}
