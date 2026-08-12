using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Evaluation.Judging;

/// <summary>One answer transcript to be judged, produced by a generation run.</summary>
public sealed record JudgeEvaluationInput
{
    /// <summary>Generation dataset id of the question.</summary>
    public required string Id { get; init; }

    /// <summary>Category of the question.</summary>
    public required string Category { get; init; }

    /// <summary>The question as asked.</summary>
    public required string Question { get; init; }

    /// <summary>The answer the pipeline produced.</summary>
    public required string Answer { get; init; }

    /// <summary>The passages the pipeline actually retrieved for it.</summary>
    public required IReadOnlyList<RetrievedChunk> Evidence { get; init; }

    /// <summary>What a complete answer was expected to cover, from the dataset.</summary>
    public IReadOnlyList<string> ExpectedFacts { get; init; } = [];
}

/// <summary>Knobs for a judge run.</summary>
public sealed record JudgeEvaluationOptions
{
    /// <summary>
    /// Minimum score every criterion must reach for a case to count as
    /// acceptable. 4 of 5 by default: a 3 is "not obviously wrong", which is not
    /// the bar a compliance reviewer would apply.
    /// </summary>
    public int AcceptanceThreshold { get; init; } = 4;

    /// <summary>Free-text label naming the judge configuration under test.</summary>
    public string? JudgeConfiguration { get; init; }

    /// <summary>Whether the judge was a scripted offline fixture or a live model.</summary>
    public EvaluationRunType RunType { get; init; } = EvaluationRunType.Fixture;

    /// <summary>Throws when the options cannot produce a coherent report.</summary>
    public void Validate()
    {
        if (AcceptanceThreshold is < Prompts.JudgePrompt.MinScore or > Prompts.JudgePrompt.MaxScore)
        {
            throw new ArgumentException(
                $"The acceptance threshold must be between {Prompts.JudgePrompt.MinScore} "
                + $"and {Prompts.JudgePrompt.MaxScore}.",
                nameof(AcceptanceThreshold));
        }
    }
}

/// <summary>
/// Runs the judge over a set of answer transcripts and compares its conclusions
/// against the manually reviewed examples.
/// </summary>
/// <remarks>
/// Taking transcripts rather than re-running the pipeline is deliberate. A live
/// judge run is the most expensive thing in the harness — a full model
/// completion per answer, on top of the completion that produced the answer —
/// and driving it from an existing run means one generation pass can feed both
/// the generation report and the judge report.
///
/// The manual-review comparison is what keeps the judge honest. An automated
/// judge is worth exactly as much as its agreement with a person on cases a
/// person has looked at, so every run reports that agreement rate next to its
/// own scores.
/// </remarks>
public sealed class JudgeEvaluationRunner
{
    private readonly IAnswerJudge _judge;
    private readonly ILogger<JudgeEvaluationRunner> _logger;

    public JudgeEvaluationRunner(IAnswerJudge judge, ILogger<JudgeEvaluationRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(judge);

        _judge = judge;
        _logger = logger ?? NullLogger<JudgeEvaluationRunner>.Instance;
    }

    /// <summary>Judges every transcript and scores agreement with the human labels.</summary>
    /// <param name="inputs">Answer transcripts from a generation run.</param>
    /// <param name="manualReviews">Human labels; may be null when none exist.</param>
    /// <param name="options">Run configuration.</param>
    /// <param name="cancellationToken">Cancels the run between and during cases.</param>
    public async Task<JudgeEvaluationReport> RunAsync(
        IReadOnlyList<JudgeEvaluationInput> inputs,
        ManualReviewDataset? manualReviews = null,
        JudgeEvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            throw new InvalidOperationException("A judge run needs at least one answer to judge.");
        }

        var runOptions = options ?? new JudgeEvaluationOptions();
        runOptions.Validate();

        var cases = new List<JudgeCaseResult>(inputs.Count);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cases.Add(await JudgeAsync(input, manualReviews, runOptions, cancellationToken));
        }

        var byCategory = cases
            .GroupBy(result => result.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => JudgeMetrics.FromResults([.. group]),
                StringComparer.Ordinal);
        var overall = JudgeMetrics.FromResults(cases);

        _logger.LogInformation(
            "Judge run scored {JudgedCount} of {CaseCount} answers ({RunType}); "
            + "mean {MeanScore}, acceptable {AcceptableRate}, manual agreement {ManualAgreementRate}.",
            overall.JudgedCount,
            overall.CaseCount,
            runOptions.RunType,
            overall.MeanScore,
            overall.AcceptableRate,
            overall.ManualAgreementRate);

        return new JudgeEvaluationReport
        {
            RunType = runOptions.RunType,
            PromptVersion = Prompts.JudgePrompt.Version,
            AcceptanceThreshold = runOptions.AcceptanceThreshold,
            JudgeConfiguration = runOptions.JudgeConfiguration,
            Overall = overall,
            ByCategory = byCategory,
            Cases = cases,
        };
    }

    private async Task<JudgeCaseResult> JudgeAsync(
        JudgeEvaluationInput input,
        ManualReviewDataset? manualReviews,
        JudgeEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        var verdict = await _judge.JudgeAsync(
            new JudgeRequest
            {
                Question = input.Question,
                Answer = input.Answer,
                Evidence = input.Evidence,
                ExpectedFacts = input.ExpectedFacts,
            },
            cancellationToken);

        // An unjudged case is excluded from the aggregates rather than counted
        // as a bad answer: the judge failing says nothing about the answer.
        bool? judgedAcceptable = verdict.Outcome == JudgeOutcome.Judged
            ? verdict.Scores.All(score => score.Score >= options.AcceptanceThreshold)
            : null;

        var manual = manualReviews?.Find(input.Id);
        bool? agrees = manual is null || judgedAcceptable is null
            ? null
            : judgedAcceptable.Value == (manual.Verdict == ManualVerdict.Acceptable);

        return new JudgeCaseResult
        {
            Id = input.Id,
            Category = input.Category,
            Question = input.Question,
            Verdict = verdict,
            JudgedAcceptable = judgedAcceptable,
            ManualVerdict = manual?.Verdict,
            AgreesWithManualReview = agrees,
        };
    }
}
