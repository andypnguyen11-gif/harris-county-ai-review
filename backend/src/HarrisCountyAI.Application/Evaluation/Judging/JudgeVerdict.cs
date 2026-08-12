namespace HarrisCountyAI.Application.Evaluation.Judging;

/// <summary>How a judging attempt concluded.</summary>
public enum JudgeOutcome
{
    /// <summary>The judge returned a usable verdict.</summary>
    Judged,

    /// <summary>
    /// The judge could not be reached, or its response did not conform to the
    /// contract. No scores should be read; the case is excluded from metrics
    /// rather than counted as a bad answer.
    /// </summary>
    UnableToJudge,
}

/// <summary>One criterion's score and the judge's one-line reason for it.</summary>
public sealed record JudgeCriterionScore
{
    /// <summary>Which criterion was scored.</summary>
    public required JudgeCriterion Criterion { get; init; }

    /// <summary>The score, 1–5, higher is better.</summary>
    public required int Score { get; init; }

    /// <summary>The judge's stated reason, so a low score can be argued with.</summary>
    public required string Reasoning { get; init; }
}

/// <summary>
/// The judge's evaluation of one answer. This is the result schema written into
/// <c>evaluation/datasets/generation/results/</c>.
/// </summary>
public sealed record JudgeVerdict
{
    /// <summary>Whether the verdict is usable.</summary>
    public required JudgeOutcome Outcome { get; init; }

    /// <summary>Per-criterion scores, in <see cref="JudgeCriterion"/> order. Empty when not judged.</summary>
    public required IReadOnlyList<JudgeCriterionScore> Scores { get; init; }

    /// <summary>Claims the judge found unsupported by the evidence, verbatim.</summary>
    public required IReadOnlyList<string> UnsupportedClaims { get; init; }

    /// <summary>The judge's short overall summary, or the reason judging failed.</summary>
    public required string Summary { get; init; }

    /// <summary>Version of the judge prompt that produced this verdict.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>Model deployment that acted as judge, when one was called.</summary>
    public string? ModelDeployment { get; init; }

    /// <summary>Score for one criterion, or null when the verdict carries none.</summary>
    public int? ScoreFor(JudgeCriterion criterion) =>
        Scores.FirstOrDefault(score => score.Criterion == criterion)?.Score;

    /// <summary>
    /// Mean of all five criteria, rounded to two decimals; null when the verdict
    /// is unusable. A blunt instrument — the per-criterion scores are what
    /// diagnose a problem — but useful for spotting movement between runs.
    /// </summary>
    public double? MeanScore => Scores.Count == 0
        ? null
        : Math.Round(Scores.Average(score => (double)score.Score), 2, MidpointRounding.AwayFromZero);
}
