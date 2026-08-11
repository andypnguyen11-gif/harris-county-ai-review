using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>
/// The outcome of a single validation rule within a <see cref="ValidationReport"/>.
/// A mutable data snapshot for the same reason as its owner — see the remarks on
/// <see cref="ValidationReport"/>.
/// </summary>
public class ValidationReportItem
{
    public Guid Id { get; set; }

    /// <summary>Zero-based position of the rule within the workflow's rule set, for stable display ordering.</summary>
    public int Order { get; set; }

    /// <summary>Name of the rule instance that produced this result, e.g. "RequiredFieldRule(Owner name)".</summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>Human-readable label of the requirement that was checked, e.g. "Applicant signature".</summary>
    public string Requirement { get; set; } = string.Empty;

    /// <summary>How the result was produced (deterministic rule code vs. semantic evaluation).</summary>
    public ValidationType ValidationType { get; set; }

    public ValidationStatus Status { get; set; }

    /// <summary>Explanation of the outcome, suitable for display to a reviewer.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Raw value extracted from the document, when one was found.</summary>
    public string? ExtractedValue { get; set; }

    /// <summary>Id of the uploaded <see cref="Document"/> the evidence came from, when applicable.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>Type of the document the evidence came from, when applicable.</summary>
    public DocumentType? DocumentType { get; set; }

    /// <summary>1-based page number of the evidence within the source document, when known.</summary>
    public int? PageNumber { get; set; }
}
