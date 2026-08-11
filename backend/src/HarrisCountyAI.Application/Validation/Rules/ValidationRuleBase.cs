using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation.Rules;

/// <summary>
/// Base for instance-configured deterministic rules. Each instance validates one requirement
/// (given as a human-readable label) and stamps every result with its own name and
/// <see cref="ValidationType.Deterministic"/>.
/// </summary>
public abstract class ValidationRuleBase : IValidationRule
{
    protected ValidationRuleBase(string requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement))
        {
            throw new ArgumentException("Requirement label is required.", nameof(requirement));
        }

        Requirement = requirement.Trim();
        Name = $"{GetType().Name}({Requirement})";
    }

    /// <summary>Human-readable label of the requirement this rule instance checks.</summary>
    public string Requirement { get; }

    public string Name { get; }

    public abstract Task<ValidationResult> ValidateAsync(ValidationContext context, CancellationToken cancellationToken);

    protected ValidationResult Result(
        ValidationStatus status,
        string message,
        string? extractedValue = null,
        Guid? sourceDocumentId = null,
        int? page = null) =>
        new()
        {
            Requirement = Requirement,
            Status = status,
            Message = message,
            ExtractedValue = extractedValue,
            SourceDocumentId = sourceDocumentId,
            Page = page,
            ValidationType = ValidationType.Deterministic,
            RuleName = Name,
        };

    /// <summary>
    /// Returns an <see cref="ValidationStatus.UnableToDetermine"/> result when the rule cannot run at all —
    /// no extracted documents, or the document type it is scoped to was never submitted — instead of guessing.
    /// Returns null when the rule can proceed.
    /// </summary>
    protected ValidationResult? GateOnDocuments(ValidationContext context, DocumentType? requiredDocumentType)
    {
        if (context.Documents.Count == 0)
        {
            return Result(
                ValidationStatus.UnableToDetermine,
                $"No extracted documents are available for this case, so '{Requirement}' cannot be checked.");
        }

        if (requiredDocumentType is { } documentType && !context.HasDocumentType(documentType))
        {
            return Result(
                ValidationStatus.UnableToDetermine,
                $"No {documentType} document was submitted, so '{Requirement}' cannot be checked.");
        }

        return null;
    }
}
