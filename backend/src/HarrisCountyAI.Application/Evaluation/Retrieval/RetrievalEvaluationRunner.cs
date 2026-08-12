using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>
/// Runs the retrieval evaluation dataset against an <see cref="IRetrievalService"/>
/// and scores the results.
/// </summary>
/// <remarks>
/// Retrieval evaluation is deliberately model-free: the runner asks the same
/// corpus-scoped retrieval the product uses, then applies deterministic
/// matching rules. That separation is the point — when a RAG answer is wrong,
/// this report says whether the evidence was ever in front of the model.
///
/// The runner never throws for a failing question. A retrieval error is
/// recorded on the case and scored as a miss, so one flaky query cannot destroy
/// the rest of a run; only caller cancellation propagates.
/// </remarks>
public sealed class RetrievalEvaluationRunner
{
    private readonly IRetrievalService _retrievalService;
    private readonly ILogger<RetrievalEvaluationRunner> _logger;

    public RetrievalEvaluationRunner(
        IRetrievalService retrievalService,
        ILogger<RetrievalEvaluationRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(retrievalService);

        _retrievalService = retrievalService;
        _logger = logger ?? NullLogger<RetrievalEvaluationRunner>.Instance;
    }

    /// <summary>Scores every question in <paramref name="dataset"/>.</summary>
    /// <param name="dataset">The committed evaluation questions.</param>
    /// <param name="options">Run configuration; defaults to top 5 with recall at 1, 3, and 5.</param>
    /// <param name="cancellationToken">Cancels the run between and during questions.</param>
    public async Task<RetrievalEvaluationReport> RunAsync(
        RetrievalEvaluationDataset dataset,
        RetrievalEvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        dataset.Validate();

        var runOptions = options ?? new RetrievalEvaluationOptions();
        runOptions.Validate();

        var cases = new List<RetrievalCaseResult>(dataset.Questions.Count);
        foreach (var question in dataset.Questions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cases.Add(await ScoreAsync(question, runOptions, cancellationToken));
        }

        var cutoffs = runOptions.RecallCutoffs;
        var byCategory = cases
            .GroupBy(result => result.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => RetrievalMetrics.FromRanks([.. group.Select(result => result.FirstMatchRank)], cutoffs),
                StringComparer.Ordinal);

        var overall = RetrievalMetrics.FromRanks([.. cases.Select(result => result.FirstMatchRank)], cutoffs);

        _logger.LogInformation(
            "Retrieval evaluation scored {QuestionCount} questions ({RunType}); "
            + "Recall@1 {RecallAt1}, Recall@3 {RecallAt3}, Recall@5 {RecallAt5}, MRR {Mrr}.",
            overall.QuestionCount,
            runOptions.RunType,
            overall.RecallAt1,
            overall.RecallAt3,
            overall.RecallAt5,
            overall.MeanReciprocalRank);

        return new RetrievalEvaluationReport
        {
            RunType = runOptions.RunType,
            DatasetVersion = dataset.Version,
            RetrievalConfiguration = runOptions.RetrievalConfiguration,
            TopK = runOptions.TopK,
            PageTolerance = runOptions.PageTolerance,
            Overall = overall,
            ByCategory = byCategory,
            Cases = cases,
        };
    }

    private async Task<RetrievalCaseResult> ScoreAsync(
        RetrievalEvaluationCase question,
        RetrievalEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RetrievedChunk> chunks;
        try
        {
            chunks = await _retrievalService.RetrieveAsync(
                new RetrievalRequest
                {
                    Query = question.Question,
                    Scope = SourceType.County,
                    TopK = options.TopK,
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Score the question as a miss rather than aborting the run: a
            // partial report with one recorded failure is more useful than none.
            _logger.LogWarning(exception, "Retrieval failed for evaluation question {QuestionId}.", question.Id);
            return new RetrievalCaseResult
            {
                Id = question.Id,
                Category = question.Category,
                Question = question.Question,
                FirstMatchRank = null,
                RetrievedCount = 0,
                Error = exception.Message,
                Retrieved = [],
            };
        }

        var retrieved = new List<RetrievedSourceSummary>(chunks.Count);
        int? firstMatchRank = null;
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var rank = index + 1;
            var isExpected = ExpectedSourceMatcher.MatchesAny(
                chunk, question.ExpectedSources, options.PageTolerance);
            if (isExpected && firstMatchRank is null)
            {
                firstMatchRank = rank;
            }

            retrieved.Add(new RetrievedSourceSummary
            {
                Rank = rank,
                Title = chunk.Title,
                Section = chunk.Section,
                Page = chunk.Page,
                Score = RetrievalMetrics.Round(chunk.Score),
                RerankerScore = chunk.RerankerScore is null ? null : RetrievalMetrics.Round(chunk.RerankerScore.Value),
                IsExpected = isExpected,
            });
        }

        return new RetrievalCaseResult
        {
            Id = question.Id,
            Category = question.Category,
            Question = question.Question,
            FirstMatchRank = firstMatchRank,
            RetrievedCount = chunks.Count,
            Retrieved = retrieved,
        };
    }
}
