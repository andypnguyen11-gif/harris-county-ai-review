namespace HarrisCountyAI.Domain.Enums;

/// <summary>How a validation result was produced.</summary>
public enum ValidationType
{
    /// <summary>Produced by deterministic rule code; no model involvement.</summary>
    Deterministic,

    /// <summary>Produced by semantic (language model) evaluation.</summary>
    Semantic,
}
