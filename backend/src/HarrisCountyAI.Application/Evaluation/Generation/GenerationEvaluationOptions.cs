namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>Knobs for a single generation evaluation run.</summary>
public sealed record GenerationEvaluationOptions
{
    /// <summary>Passages to retrieve as evidence per question.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Share of a sentence's content words that must appear in the evidence
    /// before the sentence counts as supported.
    /// </summary>
    public double SupportThreshold { get; init; } = UnsupportedClaimDetector.DefaultSupportThreshold;

    /// <summary>Free-text label naming the pipeline configuration under test.</summary>
    public string? PipelineConfiguration { get; init; }

    /// <summary>Whether the run used a scripted offline model or the live deployment.</summary>
    public EvaluationRunType RunType { get; init; } = EvaluationRunType.Fixture;

    /// <summary>Throws when the options cannot produce a coherent report.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(TopK, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(SupportThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SupportThreshold, 1d);
    }
}
