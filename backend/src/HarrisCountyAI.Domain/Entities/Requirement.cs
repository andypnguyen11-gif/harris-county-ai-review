using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>
/// One thing Harris County requires of a submission, expressed so that as much
/// of it as possible can be checked deterministically.
/// </summary>
/// <remarks>
/// A requirement is authored by this codebase from the published county
/// regulations — never inferred by a model and never read out of an applicant's
/// document — so its text is trusted and safe to put in prompt instruction
/// framing.
///
/// The shape deliberately separates the mechanical part of a requirement from
/// the judgment part. <see cref="RequiredDocumentType"/> and
/// <see cref="RequiredFieldNames"/> are facts about presence that code can
/// settle by itself; <see cref="SemanticCriterion"/> is the part, if any, that
/// only makes sense as "does what was submitted actually satisfy this?". A
/// requirement with no semantic criterion is decided entirely in code.
/// </remarks>
public sealed record Requirement
{
    /// <summary>Stable identifier for this requirement, e.g. <c>site-plan</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The workflow this requirement belongs to.</summary>
    public required WorkflowType WorkflowType { get; init; }

    /// <summary>Short human-readable label, e.g. "Site plan".</summary>
    public required string Label { get; init; }

    /// <summary>
    /// What the county requires, in prose, for display to a reviewer and as the
    /// query used to pull supporting passages from the reference corpus.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Where the requirement comes from, e.g. "Floodplain Management
    /// Regulations Sec. 4.04(b)".
    /// </summary>
    public string? SourceReference { get; init; }

    /// <summary>
    /// The document that must be present, when the requirement is about a
    /// document at all. Checked deterministically.
    /// </summary>
    public DocumentType? RequiredDocumentType { get; init; }

    /// <summary>
    /// Field name variants that must be present and non-blank on the required
    /// document. Checked deterministically; matching ignores case, whitespace,
    /// and punctuation, so OCR renderings of the same printed label compare
    /// equal. Any one variant matching satisfies the check.
    /// </summary>
    public IReadOnlyList<string> RequiredFieldNames { get; init; } = [];

    /// <summary>
    /// The judgment a reviewer would have to make once everything mechanical
    /// checks out, or null when presence alone settles the requirement. Only a
    /// requirement with a criterion is ever eligible for semantic evaluation,
    /// and only after its deterministic checks have passed.
    /// </summary>
    public string? SemanticCriterion { get; init; }

    /// <summary>
    /// Whether the requirement applies only in circumstances the extracted data
    /// cannot establish (for example a permit class). Absence of a conditional
    /// requirement is reported for human review rather than as a plain
    /// omission, because code cannot tell whether it was owed.
    /// </summary>
    public bool IsConditional { get; init; }
}
