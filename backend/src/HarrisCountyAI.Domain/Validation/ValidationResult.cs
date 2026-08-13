using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.Domain.Validation;

/// <summary>Outcome of evaluating one requirement against a case's submitted documents.</summary>
public sealed class ValidationResult
{
    /// <summary>Human-readable label of the requirement being checked, e.g. "Applicant signature".</summary>
    public required string Requirement { get; init; }

    public required ValidationStatus Status { get; init; }

    /// <summary>Raw value extracted from the document, when one was found.</summary>
    public string? ExtractedValue { get; init; }

    /// <summary>Explanation of the outcome, suitable for display to a reviewer.</summary>
    public required string Message { get; init; }

    /// <summary>Id of the normalized document the evidence came from, when applicable.</summary>
    public Guid? SourceDocumentId { get; init; }

    /// <summary>Page number of the evidence within the source document, when known.</summary>
    public int? Page { get; init; }

    public required ValidationType ValidationType { get; init; }

    /// <summary>Name of the rule instance that produced this result.</summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// Region of the source document the evidence was read from, when it could
    /// be located. Null means no region was reported — the finding cannot be
    /// pointed at on the page, and must say so rather than guess.
    /// </summary>
    public BoundingBox? BoundingBox { get; init; }
}
