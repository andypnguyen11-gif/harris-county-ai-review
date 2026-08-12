namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// Generation quality over a set of evaluation questions. Every rate is 0.0 to
/// 1.0 and rounded to four decimals so committed baselines diff cleanly; a rate
/// with no applicable questions is null rather than zero, because "no data" and
/// "zero percent" are very different findings.
/// </summary>
public sealed record GenerationMetrics
{
    /// <summary>Number of questions the metrics cover.</summary>
    public required int QuestionCount { get; init; }

    /// <summary>Share of questions where the pipeline concluded the way the dataset expected.</summary>
    public required double OutcomeMatchRate { get; init; }

    /// <summary>
    /// Share of answered responses carrying at least one citation. Null when
    /// nothing was answered. The pipeline is supposed to make this impossible to
    /// fail — an answer with no resolvable citation is downgraded to
    /// insufficient evidence — so anything below 1.0 is a defect, not a score.
    /// </summary>
    public required double? CitationPresenceRate { get; init; }

    /// <summary>
    /// Share of answered responses whose citations all named a document the
    /// dataset expected. Null when no answered question recorded title
    /// expectations.
    /// </summary>
    public required double? CitationTitleAccuracy { get; init; }

    /// <summary>Mean share of expected facts stated, over questions that list facts. Null when none do.</summary>
    public required double? MeanFactCoverage { get; init; }

    /// <summary>Share of fact-bearing questions whose answer stated every expected fact.</summary>
    public required double? FullFactCoverageRate { get; init; }

    /// <summary>
    /// Share of analyzed answer sentences that fell below the lexical support
    /// threshold. Null when no evidence was recorded, so the check did not run.
    /// </summary>
    public required double? UnsupportedClaimRate { get; init; }

    /// <summary>Share of answers containing at least one unsupported sentence.</summary>
    public required double? AnswersWithUnsupportedClaimsRate { get; init; }

    /// <summary>Computes the metrics from per-question results.</summary>
    public static GenerationMetrics FromResults(IReadOnlyList<GenerationCaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var answered = results
            .Where(result => result.ActualOutcome == QuestionAnswering.QuestionAnswerOutcome.Answered)
            .ToList();
        var withFacts = results.Where(result => result.FactCoverage is not null).ToList();
        var withTitleExpectations = answered.Where(result => result.CitationTitlesMatched is not null).ToList();
        var analyzed = results.Where(result => result.Claims is not null).ToList();
        var allClaims = analyzed.SelectMany(result => result.Claims!).ToList();

        return new GenerationMetrics
        {
            QuestionCount = results.Count,
            OutcomeMatchRate = Rate(results.Count(result => result.OutcomeMatched), results.Count) ?? 0d,
            CitationPresenceRate = Rate(answered.Count(result => result.Citations.Count > 0), answered.Count),
            CitationTitleAccuracy = Rate(
                withTitleExpectations.Count(result => result.CitationTitlesMatched == true),
                withTitleExpectations.Count),
            MeanFactCoverage = withFacts.Count == 0
                ? null
                : Round(withFacts.Average(result => result.FactCoverage!.Value)),
            FullFactCoverageRate = Rate(withFacts.Count(result => result.FactCoverage >= 1d), withFacts.Count),
            UnsupportedClaimRate = Rate(allClaims.Count(claim => !claim.IsSupported), allClaims.Count),
            AnswersWithUnsupportedClaimsRate = Rate(
                analyzed.Count(result => result.UnsupportedClaims.Count > 0), analyzed.Count),
        };
    }

    private static double? Rate(int numerator, int denominator) =>
        denominator == 0 ? null : Round((double)numerator / denominator);

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
