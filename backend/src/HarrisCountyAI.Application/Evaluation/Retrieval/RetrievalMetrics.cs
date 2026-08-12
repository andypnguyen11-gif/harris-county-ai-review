namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>
/// Retrieval quality over a set of evaluation questions.
/// </summary>
/// <remarks>
/// Each question in this dataset has a single correct piece of evidence (any one
/// of its listed alternatives), so Recall@K reduces to "the share of questions
/// whose expected source appears somewhere in the top K" — the figure the PRD
/// asks for. <see cref="MeanReciprocalRank"/> adds the ordering information that
/// Recall@K throws away: two configurations can both find the evidence in the
/// top 5 while one consistently ranks it first.
/// </remarks>
public sealed record RetrievalMetrics
{
    /// <summary>Number of questions the metrics were computed over.</summary>
    public required int QuestionCount { get; init; }

    /// <summary>Number of questions where an expected source appeared anywhere in the retrieved results.</summary>
    public required int HitCount { get; init; }

    /// <summary>Recall keyed by cutoff — 0.0 to 1.0, rounded to four decimals for stable diffs.</summary>
    public required IReadOnlyDictionary<int, double> RecallAtK { get; init; }

    /// <summary>Mean reciprocal rank of the first expected source; a miss contributes 0.</summary>
    public required double MeanReciprocalRank { get; init; }

    /// <summary>Recall at the top result, or null when the run did not report that cutoff.</summary>
    public double? RecallAt1 => RecallAtK.TryGetValue(1, out var value) ? value : null;

    /// <summary>Recall in the top three results, or null when the run did not report that cutoff.</summary>
    public double? RecallAt3 => RecallAtK.TryGetValue(3, out var value) ? value : null;

    /// <summary>Recall in the top five results, or null when the run did not report that cutoff.</summary>
    public double? RecallAt5 => RecallAtK.TryGetValue(5, out var value) ? value : null;

    /// <summary>
    /// Computes the metrics from the rank of the first expected source in each
    /// question's results (1-based; null when the expected source was not
    /// retrieved at all).
    /// </summary>
    public static RetrievalMetrics FromRanks(IReadOnlyList<int?> firstMatchRanks, IReadOnlyList<int> cutoffs)
    {
        ArgumentNullException.ThrowIfNull(firstMatchRanks);
        ArgumentNullException.ThrowIfNull(cutoffs);

        var count = firstMatchRanks.Count;
        var recall = new Dictionary<int, double>();
        foreach (var cutoff in cutoffs.Distinct().OrderBy(cutoff => cutoff))
        {
            var hits = firstMatchRanks.Count(rank => rank is not null && rank <= cutoff);
            recall[cutoff] = Round(count == 0 ? 0d : (double)hits / count);
        }

        var reciprocalSum = firstMatchRanks.Sum(rank => rank is null ? 0d : 1d / rank.Value);

        return new RetrievalMetrics
        {
            QuestionCount = count,
            HitCount = firstMatchRanks.Count(rank => rank is not null),
            RecallAtK = recall,
            MeanReciprocalRank = Round(count == 0 ? 0d : reciprocalSum / count),
        };
    }

    /// <summary>Four decimals is finer than this dataset can resolve, and keeps committed baselines diff-stable.</summary>
    internal static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
