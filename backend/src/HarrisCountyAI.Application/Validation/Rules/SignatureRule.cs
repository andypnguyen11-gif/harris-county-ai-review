using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.Application.Validation.Rules;

/// <summary>
/// Requires a signature field to be present and signed. A signature field that is present but
/// unsigned, or not found at all, reports <see cref="ValidationStatus.Missing"/>.
/// </summary>
public sealed class SignatureRule : ValidationRuleBase
{
    private readonly IReadOnlyCollection<string> _fieldNames;
    private readonly DocumentType? _documentType;

    public SignatureRule(
        string requirement,
        string fieldName,
        IEnumerable<string>? nameVariants = null,
        DocumentType? documentType = null)
        : base(requirement)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        }

        _fieldNames = [fieldName, .. nameVariants ?? []];
        _documentType = documentType;
    }

    public override Task<ValidationResult> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        if (GateOnDocuments(context, _documentType) is { } gated)
        {
            return Task.FromResult(gated);
        }

        var match = context.FindField(_fieldNames, _documentType);
        if (match is null)
        {
            return Task.FromResult(Result(
                ValidationStatus.Missing,
                $"Signature field '{_fieldNames.First()}' was not found in the submitted documents."));
        }

        var markBox = match.Field.ValueBoundingBox ?? match.Field.KeyBoundingBox;

        if (match.Field.IsSigned == true)
        {
            return Task.FromResult(Result(
                ValidationStatus.Complete,
                $"Signature field '{match.Field.Name}' is signed.",
                extractedValue: match.Field.Value,
                sourceDocumentId: match.Document.Id,
                page: markBox?.PageNumber ?? match.Field.PageNumber,
                boundingBox: markBox));
        }

        return Task.FromResult(Result(
            ValidationStatus.Missing,
            $"Signature field '{match.Field.Name}' is present but not signed.",
            sourceDocumentId: match.Document.Id,
            page: markBox?.PageNumber ?? match.Field.PageNumber,
            boundingBox: markBox));
    }
}
