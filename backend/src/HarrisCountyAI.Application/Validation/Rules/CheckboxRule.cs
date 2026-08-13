using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.Application.Validation.Rules;

/// <summary>
/// Requires at least one checkbox in a configured set to be checked. A single-checkbox
/// configuration means that box itself must be checked; a multi-checkbox configuration models
/// "check at least one applicable box" sections. An optional applicability condition supports
/// checkboxes that are only required in certain circumstances.
/// </summary>
public sealed class CheckboxRule : ValidationRuleBase
{
    private readonly IReadOnlyList<IReadOnlyCollection<string>> _checkboxes;
    private readonly DocumentType? _documentType;
    private readonly Func<ValidationContext, bool>? _applicableWhen;

    /// <summary>Requires the single named checkbox (with optional OCR name variants) to be checked.</summary>
    public CheckboxRule(
        string requirement,
        string fieldName,
        IEnumerable<string>? nameVariants = null,
        DocumentType? documentType = null,
        Func<ValidationContext, bool>? applicableWhen = null)
        : this(requirement, [[fieldName, .. nameVariants ?? []]], documentType, applicableWhen)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        }
    }

    /// <summary>Requires at least one of the given checkboxes (each a set of name variants for one box) to be checked.</summary>
    public CheckboxRule(
        string requirement,
        IReadOnlyList<IReadOnlyCollection<string>> checkboxes,
        DocumentType? documentType = null,
        Func<ValidationContext, bool>? applicableWhen = null)
        : base(requirement)
    {
        if (checkboxes is null || checkboxes.Count == 0)
        {
            throw new ArgumentException("At least one checkbox must be configured.", nameof(checkboxes));
        }

        _checkboxes = checkboxes;
        _documentType = documentType;
        _applicableWhen = applicableWhen;
    }

    public override Task<ValidationResult> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        if (_applicableWhen is not null && !_applicableWhen(context))
        {
            return Task.FromResult(Result(
                ValidationStatus.Complete,
                $"Not applicable: the condition for '{Requirement}' is not met by this submission."));
        }

        if (GateOnDocuments(context, _documentType) is { } gated)
        {
            return Task.FromResult(gated);
        }

        FieldMatch? firstFound = null;
        foreach (var nameVariants in _checkboxes)
        {
            var match = context.FindField(nameVariants, _documentType);
            if (match is null)
            {
                continue;
            }

            firstFound ??= match;

            if (match.Field.IsChecked == true)
            {
                var checkedBox = match.Field.ValueBoundingBox ?? match.Field.KeyBoundingBox;
                return Task.FromResult(Result(
                    ValidationStatus.Complete,
                    $"Checkbox '{match.Field.Name}' is checked.",
                    extractedValue: match.Field.Name,
                    sourceDocumentId: match.Document.Id,
                    page: checkedBox?.PageNumber ?? match.Field.PageNumber,
                    boundingBox: checkedBox));
            }
        }

        if (firstFound is { } found)
        {
            var foundBox = found.Field.ValueBoundingBox ?? found.Field.KeyBoundingBox;
            return Task.FromResult(Result(
                ValidationStatus.Missing,
                _checkboxes.Count == 1
                    ? $"Checkbox '{_checkboxes[0].First()}' is present but not checked."
                    : $"None of the checkboxes for '{Requirement}' are checked.",
                sourceDocumentId: found.Document.Id,
                page: foundBox?.PageNumber ?? found.Field.PageNumber,
                boundingBox: foundBox));
        }

        return Task.FromResult(Result(
            ValidationStatus.Missing,
            _checkboxes.Count == 1
                ? $"Checkbox '{_checkboxes[0].First()}' was not found in the submitted documents."
                : $"No checkboxes for '{Requirement}' were found in the submitted documents."));
    }
}
