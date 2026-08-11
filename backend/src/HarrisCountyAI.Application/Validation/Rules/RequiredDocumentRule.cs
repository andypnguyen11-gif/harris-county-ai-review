using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation.Rules;

/// <summary>
/// Requires at least one document of a given type in the submission package. The status and
/// message reported when the document is absent are configurable, so conditionally required
/// documents can surface as <see cref="ValidationStatus.NeedsHumanReview"/> instead of
/// <see cref="ValidationStatus.Missing"/>.
/// </summary>
public sealed class RequiredDocumentRule : ValidationRuleBase
{
    private readonly DocumentType _documentType;
    private readonly ValidationStatus _missingStatus;
    private readonly string? _missingMessage;

    public RequiredDocumentRule(
        DocumentType documentType,
        string requirement,
        ValidationStatus missingStatus = ValidationStatus.Missing,
        string? missingMessage = null)
        : base(requirement)
    {
        _documentType = documentType;
        _missingStatus = missingStatus;
        _missingMessage = missingMessage;
    }

    public override Task<ValidationResult> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var documents = context.GetDocuments(_documentType);
        if (documents.Count == 0)
        {
            return Task.FromResult(Result(
                _missingStatus,
                _missingMessage ?? $"No {_documentType} document was found in the submission package."));
        }

        var document = documents[0];
        return Task.FromResult(Result(
            ValidationStatus.Complete,
            documents.Count == 1
                ? $"A {_documentType} document is present."
                : $"{documents.Count} {_documentType} documents are present.",
            sourceDocumentId: document.Id));
    }
}
