namespace HarrisCountyAI.Application.Validation.Semantic;

/// <summary>A single semantic evaluation: does the given document content satisfy the given requirement?</summary>
public sealed record SemanticValidationRequest
{
    /// <summary>Human-readable label of the requirement, e.g. "Project description consistency".</summary>
    public required string Requirement { get; init; }

    /// <summary>
    /// Trusted description of the county requirement the model evaluates against. Authored by
    /// this codebase (never from an applicant document) — it becomes part of the prompt's
    /// instruction framing.
    /// </summary>
    public required string RequirementDescription { get; init; }

    /// <summary>
    /// Untrusted content extracted from the applicant's documents. Treated strictly as data:
    /// it is delimited in the prompt and the model is instructed to ignore any instructions
    /// that appear inside it.
    /// </summary>
    public required string DocumentText { get; init; }
}
