using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>A request to compare one case's submitted documents against the requirements of a workflow.</summary>
public sealed record RequirementComparisonRequest
{
    /// <summary>The case being reviewed.</summary>
    public required Guid CaseId { get; init; }

    /// <summary>Which workflow's requirements to compare against.</summary>
    public required WorkflowType WorkflowType { get; init; }

    /// <summary>The case's normalized documents — the submission side of the comparison.</summary>
    public required IReadOnlyList<NormalizedDocument> Documents { get; init; }

    /// <summary>
    /// Whether to pull supporting passages from the county reference corpus for
    /// each requirement. Turning this off skips retrieval entirely; it never
    /// changes a verdict, because verdicts come from the requirement catalog
    /// and the submitted documents, not from retrieved text.
    /// </summary>
    public bool IncludeRequirementEvidence { get; init; } = true;

    /// <summary>
    /// Whether semantic evaluation may run for requirements whose mechanical
    /// checks passed but which carry a judgment criterion. Turning this off
    /// yields a purely deterministic comparison; requirements that need
    /// judgment report <see cref="ValidationStatus.NeedsHumanReview"/>.
    /// </summary>
    public bool AllowSemanticEvaluation { get; init; } = true;

    /// <summary>Number of corpus passages to attach per requirement.</summary>
    public int EvidencePerRequirement { get; init; } = 3;
}
