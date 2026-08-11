namespace HarrisCountyAI.Application.Common.AI;

/// <summary>A provider-agnostic request for a single language model generation.</summary>
public sealed record ModelRequest
{
    /// <summary>Instructions that frame the model's role and constraints.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>The task-specific input for this generation.</summary>
    public required string UserPrompt { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the caller expects structured JSON output and the
    /// provider is instructed to constrain the response to valid JSON.
    /// </summary>
    public bool ExpectsJsonResponse { get; init; }

    /// <summary>
    /// Optional name of the JSON schema the caller expects the response to conform to.
    /// Recorded for observability; only meaningful when <see cref="ExpectsJsonResponse"/> is set.
    /// </summary>
    public string? JsonResponseSchemaName { get; init; }

    /// <summary>
    /// Maximum number of output tokens to generate. When <see langword="null"/>, the
    /// configured provider default applies.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Sampling temperature. Defaults low (0.1) because this product performs
    /// validation and compliance checks where determinism matters more than creativity.
    /// </summary>
    public float Temperature { get; init; } = 0.1f;

    /// <summary>
    /// Version identifier of the prompt that produced this request, for observability
    /// and later correlation of model behavior with prompt changes.
    /// </summary>
    public string? PromptVersion { get; init; }
}
