namespace HarrisCountyAI.Application.Evaluation.Judging;

/// <summary>
/// What the judge said about one answer, plus the human label when the case was
/// manually reviewed.
/// </summary>
public sealed record JudgeCaseResult
{
    /// <summary>Generation dataset id of the question.</summary>
    public required string Id { get; init; }

    /// <summary>Category of the question.</summary>
    public required string Category { get; init; }

    /// <summary>The question as asked.</summary>
    public required string Question { get; init; }

    /// <summary>The judge's verdict.</summary>
    public required JudgeVerdict Verdict { get; init; }

    /// <summary>
    /// Whether the judge's scores clear the acceptance threshold — the automated
    /// stand-in for "a reviewer would accept this". Null when the case could not
    /// be judged.
    /// </summary>
    public required bool? JudgedAcceptable { get; init; }

    /// <summary>The human verdict, when this case was manually reviewed.</summary>
    public ManualVerdict? ManualVerdict { get; init; }

    /// <summary>
    /// Whether the judge and the human reached the same conclusion. Null when
    /// the case was not manually reviewed or could not be judged.
    /// </summary>
    public bool? AgreesWithManualReview { get; init; }
}

/// <summary>Aggregate judge scores over a set of cases.</summary>
public sealed record JudgeMetrics
{
    /// <summary>Number of cases the judge was asked about.</summary>
    public required int CaseCount { get; init; }

    /// <summary>Number the judge produced a usable verdict for.</summary>
    public required int JudgedCount { get; init; }

    /// <summary>Mean score per criterion over judged cases, on the shared 1–5 scale.</summary>
    public required IReadOnlyDictionary<string, double> MeanScoreByCriterion { get; init; }

    /// <summary>Mean of the per-case means over judged cases; null when nothing was judged.</summary>
    public required double? MeanScore { get; init; }

    /// <summary>Share of judged cases clearing the acceptance threshold; null when nothing was judged.</summary>
    public required double? AcceptableRate { get; init; }

    /// <summary>Total unsupported claims the judge listed across judged cases.</summary>
    public required int UnsupportedClaimCount { get; init; }

    /// <summary>
    /// Share of manually reviewed cases where the judge reached the same
    /// conclusion as the human; null when no reviewed case was judged.
    /// </summary>
    public required double? ManualAgreementRate { get; init; }

    /// <summary>Number of manually reviewed cases the agreement rate was computed over.</summary>
    public required int ManuallyReviewedCount { get; init; }

    /// <summary>Computes the metrics from per-case results.</summary>
    public static JudgeMetrics FromResults(IReadOnlyList<JudgeCaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var judged = results
            .Where(result => result.Verdict.Outcome == JudgeOutcome.Judged)
            .ToList();
        var compared = results.Where(result => result.AgreesWithManualReview is not null).ToList();

        var byCriterion = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var criterion in Enum.GetValues<JudgeCriterion>())
        {
            var scores = judged
                .Select(result => result.Verdict.ScoreFor(criterion))
                .Where(score => score is not null)
                .Select(score => (double)score!.Value)
                .ToList();
            if (scores.Count > 0)
            {
                byCriterion[criterion.ToString()] = Round(scores.Average());
            }
        }

        return new JudgeMetrics
        {
            CaseCount = results.Count,
            JudgedCount = judged.Count,
            MeanScoreByCriterion = byCriterion,
            MeanScore = judged.Count == 0
                ? null
                : Round(judged.Average(result => result.Verdict.MeanScore!.Value)),
            AcceptableRate = judged.Count == 0
                ? null
                : Round((double)judged.Count(result => result.JudgedAcceptable == true) / judged.Count),
            UnsupportedClaimCount = judged.Sum(result => result.Verdict.UnsupportedClaims.Count),
            ManualAgreementRate = compared.Count == 0
                ? null
                : Round((double)compared.Count(result => result.AgreesWithManualReview == true) / compared.Count),
            ManuallyReviewedCount = compared.Count,
        };
    }

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The result of one judge run. Written to
/// <c>evaluation/datasets/generation/results/</c> alongside the generation
/// report it grades.
/// </summary>
public sealed record JudgeEvaluationReport
{
    /// <summary>Schema version of the report file.</summary>
    public int ReportVersion { get; init; } = 1;

    /// <summary>Whether the judge was a scripted offline fixture or a live model.</summary>
    public required EvaluationRunType RunType { get; init; }

    /// <summary>Version of the judge prompt used.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>Minimum per-criterion score required for a case to count as acceptable.</summary>
    public required int AcceptanceThreshold { get; init; }

    /// <summary>Label naming the judge configuration under test, when the run recorded one.</summary>
    public string? JudgeConfiguration { get; init; }

    /// <summary>Metrics across every case.</summary>
    public required JudgeMetrics Overall { get; init; }

    /// <summary>Metrics per question category, ordered by category name.</summary>
    public required IReadOnlyDictionary<string, JudgeMetrics> ByCategory { get; init; }

    /// <summary>Per-case detail, in input order.</summary>
    public required IReadOnlyList<JudgeCaseResult> Cases { get; init; }
}
