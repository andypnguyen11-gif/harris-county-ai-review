using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>
/// The outcome of comparing one requirement against one case's submission,
/// with both sides of the comparison attached: the county passages that
/// support the requirement, and the submitted content the check looked at.
/// </summary>
/// <remarks>
/// The result deliberately records not just what was decided but how. A
/// reviewer — and a test — can see from <see cref="EvaluatedBy"/> and
/// <see cref="DeterministicStatus"/> whether a model was involved at all, and
/// what code had already concluded before it was.
/// </remarks>
public sealed record RequirementComparisonResult
{
    /// <summary>The requirement this result is about.</summary>
    public required Requirement Requirement { get; init; }

    /// <summary>The final status of the comparison.</summary>
    public required ValidationStatus Status { get; init; }

    /// <summary>Explanation of the outcome, suitable for display to a reviewer.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// What produced <see cref="Status"/>: deterministic code, or semantic
    /// evaluation on top of a deterministic pass.
    /// </summary>
    public required ValidationType EvaluatedBy { get; init; }

    /// <summary>
    /// What the deterministic checks alone concluded, before any semantic
    /// evaluation. Equal to <see cref="Status"/> whenever
    /// <see cref="EvaluatedBy"/> is
    /// <see cref="ValidationType.Deterministic"/>.
    /// </summary>
    public required ValidationStatus DeterministicStatus { get; init; }

    /// <summary>County corpus passages supporting the requirement; empty when none were retrieved.</summary>
    public required IReadOnlyList<RequirementEvidence> RequirementEvidence { get; init; }

    /// <summary>Submitted content the check examined; empty when nothing relevant was submitted.</summary>
    public required IReadOnlyList<SubmissionEvidence> SubmissionEvidence { get; init; }

    /// <summary>Version of the semantic prompt used, when semantic evaluation ran.</summary>
    public string? PromptVersion { get; init; }

    /// <summary>The model deployment that produced the semantic judgment, when one ran.</summary>
    public string? ModelDeployment { get; init; }
}
