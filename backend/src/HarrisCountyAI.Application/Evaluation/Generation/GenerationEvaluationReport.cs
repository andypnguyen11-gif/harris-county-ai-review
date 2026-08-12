namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// The result of one generation evaluation run. Written to
/// <c>evaluation/datasets/generation/results/</c>.
/// </summary>
public sealed record GenerationEvaluationReport
{
    /// <summary>Schema version of the report file.</summary>
    public int ReportVersion { get; init; } = 1;

    /// <summary>
    /// Whether the answers came from a scripted offline model or from the live
    /// deployment. Read this before comparing two reports.
    /// </summary>
    public required EvaluationRunType RunType { get; init; }

    /// <summary>Version of the dataset the run scored.</summary>
    public required int DatasetVersion { get; init; }

    /// <summary>Label naming the pipeline configuration under test, when the run recorded one.</summary>
    public string? PipelineConfiguration { get; init; }

    /// <summary>Passages requested as evidence per question.</summary>
    public required int TopK { get; init; }

    /// <summary>Lexical support threshold applied to unsupported-claim detection.</summary>
    public required double SupportThreshold { get; init; }

    /// <summary>Metrics across every question.</summary>
    public required GenerationMetrics Overall { get; init; }

    /// <summary>Metrics per category, ordered by category name.</summary>
    public required IReadOnlyDictionary<string, GenerationMetrics> ByCategory { get; init; }

    /// <summary>Per-question detail, in dataset order.</summary>
    public required IReadOnlyList<GenerationCaseResult> Cases { get; init; }
}
