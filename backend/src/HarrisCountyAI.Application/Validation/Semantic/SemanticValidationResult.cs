namespace HarrisCountyAI.Application.Validation.Semantic;

/// <summary>Outcome of one semantic evaluation.</summary>
public sealed record SemanticValidationResult
{
    public required SemanticVerdict Verdict { get; init; }

    /// <summary>The model's short reasoning, or an explanation of why the evaluation could not run.</summary>
    public required string Reasoning { get; init; }

    /// <summary>Version of the prompt that produced this result.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>The deployment that served the request, when a model call completed.</summary>
    public string? ModelDeployment { get; init; }
}
