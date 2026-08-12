namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>Knobs for a single retrieval evaluation run.</summary>
public sealed record RetrievalEvaluationOptions
{
    /// <summary>Recall cutoffs reported by default — the three the PRD calls for.</summary>
    public static readonly IReadOnlyList<int> DefaultRecallCutoffs = [1, 3, 5];

    /// <summary>
    /// How many chunks to retrieve per question. Must be at least the largest
    /// cutoff, otherwise the deepest recall figure would be measured against a
    /// truncated result list.
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>Cutoffs to report recall at; duplicates and order do not matter.</summary>
    public IReadOnlyList<int> RecallCutoffs { get; init; } = DefaultRecallCutoffs;

    /// <summary>Page tolerance applied when an expectation records a page.</summary>
    public int PageTolerance { get; init; } = 1;

    /// <summary>
    /// Free-text label naming the retrieval configuration under test — for
    /// example "hybrid + semantic reranking". Recorded in the report so two
    /// result files can be compared meaningfully.
    /// </summary>
    public string? RetrievalConfiguration { get; init; }

    /// <summary>Whether the run measured a deterministic fixture or live Azure.</summary>
    public EvaluationRunType RunType { get; init; } = EvaluationRunType.Fixture;

    /// <summary>Throws when the options cannot produce a coherent report.</summary>
    /// <exception cref="ArgumentException">A cutoff is out of range or exceeds <see cref="TopK"/>.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(TopK, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(PageTolerance);

        if (RecallCutoffs is null || RecallCutoffs.Count == 0)
        {
            throw new ArgumentException("At least one recall cutoff is required.", nameof(RecallCutoffs));
        }

        foreach (var cutoff in RecallCutoffs)
        {
            if (cutoff < 1)
            {
                throw new ArgumentException("Recall cutoffs must be at least 1.", nameof(RecallCutoffs));
            }

            if (cutoff > TopK)
            {
                throw new ArgumentException(
                    $"Recall@{cutoff} cannot be measured from only {TopK} retrieved chunks.",
                    nameof(RecallCutoffs));
            }
        }
    }
}
