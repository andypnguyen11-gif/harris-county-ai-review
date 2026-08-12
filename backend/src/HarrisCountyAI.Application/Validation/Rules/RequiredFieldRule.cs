using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.Application.Validation.Rules;

/// <summary>
/// Requires a named field to be present with a non-empty value. Configured with the field's
/// canonical name plus OCR name variants, and optionally scoped to one document type.
/// </summary>
public sealed class RequiredFieldRule : ValidationRuleBase
{
    private readonly IReadOnlyCollection<string> _fieldNames;
    private readonly DocumentType? _documentType;

    public RequiredFieldRule(
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
                $"Field '{_fieldNames.First()}' was not found in the submitted documents."));
        }

        if (string.IsNullOrWhiteSpace(match.Field.Value))
        {
            // The printed label is what a reviewer scans for; a blank field's
            // value region is absent or too small to see.
            var labelBox = match.Field.KeyBoundingBox ?? match.Field.ValueBoundingBox;
            return Task.FromResult(Result(
                ValidationStatus.Missing,
                $"Field '{match.Field.Name}' is present but has no value.",
                sourceDocumentId: match.Document.Id,
                page: labelBox?.PageNumber ?? match.Field.PageNumber,
                boundingBox: labelBox));
        }

        var valueBox = match.Field.ValueBoundingBox ?? match.Field.KeyBoundingBox;
        return Task.FromResult(Result(
            ValidationStatus.Complete,
            $"Field '{match.Field.Name}' is present.",
            extractedValue: match.Field.Value,
            sourceDocumentId: match.Document.Id,
            page: valueBox?.PageNumber ?? match.Field.PageNumber,
            boundingBox: valueBox));
    }
}
