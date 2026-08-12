namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>
/// The result of one retrieval evaluation run: overall and per-category
/// metrics plus the per-question detail behind them. This is the shape written
/// to <c>evaluation/datasets/retrieval/results/</c>.
/// </summary>
public sealed record RetrievalEvaluationReport
{
    /// <summary>Schema version of the report file itself.</summary>
    public int ReportVersion { get; init; } = 1;

    /// <summary>
    /// Whether the numbers came from the deterministic offline fixture or from
    /// live Azure. Read this before comparing two reports.
    /// </summary>
    public required EvaluationRunType RunType { get; init; }

    /// <summary>Version of the dataset the run scored.</summary>
    public required int DatasetVersion { get; init; }

    /// <summary>Label naming the retrieval configuration under test, when the run recorded one.</summary>
    public string? RetrievalConfiguration { get; init; }

    /// <summary>Chunks requested per question.</summary>
    public required int TopK { get; init; }

    /// <summary>Page tolerance the scorer applied.</summary>
    public required int PageTolerance { get; init; }

    /// <summary>Metrics across every question in the dataset.</summary>
    public required RetrievalMetrics Overall { get; init; }

    /// <summary>Metrics per question category, ordered by category name.</summary>
    public required IReadOnlyDictionary<string, RetrievalMetrics> ByCategory { get; init; }

    /// <summary>Per-question detail, in dataset order.</summary>
    public required IReadOnlyList<RetrievalCaseResult> Cases { get; init; }
}
