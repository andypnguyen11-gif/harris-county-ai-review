using System.Globalization;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation.Rules;

/// <summary>
/// Requires a named field to contain a parseable date, with optional not-in-the-future and
/// not-older-than constraints. The clock is injectable for testability.
/// </summary>
public sealed class DateRule : ValidationRuleBase
{
    private static readonly string[] ExplicitFormats =
    [
        "M/d/yyyy",
        "M-d-yyyy",
        "M/d/yy",
        "yyyy-MM-dd",
        "MMMM d, yyyy",
        "MMM d, yyyy",
    ];

    private static readonly CultureInfo UnitedStatesCulture = CultureInfo.GetCultureInfo("en-US");

    private readonly IReadOnlyCollection<string> _fieldNames;
    private readonly DocumentType? _documentType;
    private readonly bool _disallowFuture;
    private readonly TimeSpan? _maxAge;
    private readonly Func<DateTime> _utcNow;

    public DateRule(
        string requirement,
        string fieldName,
        IEnumerable<string>? nameVariants = null,
        DocumentType? documentType = null,
        bool disallowFuture = false,
        TimeSpan? maxAge = null,
        Func<DateTime>? utcNow = null)
        : base(requirement)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        }

        _fieldNames = [fieldName, .. nameVariants ?? []];
        _documentType = documentType;
        _disallowFuture = disallowFuture;
        _maxAge = maxAge;
        _utcNow = utcNow ?? (static () => DateTime.UtcNow);
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
                $"Date field '{_fieldNames.First()}' was not found in the submitted documents."));
        }

        var rawValue = match.Field.Value;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Task.FromResult(Result(
                ValidationStatus.Missing,
                $"Date field '{match.Field.Name}' is present but has no value.",
                sourceDocumentId: match.Document.Id,
                page: match.Field.PageNumber));
        }

        if (!TryParseDate(rawValue.Trim(), out var date))
        {
            return Task.FromResult(Result(
                ValidationStatus.Invalid,
                $"Value '{rawValue}' in field '{match.Field.Name}' could not be read as a date.",
                extractedValue: rawValue,
                sourceDocumentId: match.Document.Id,
                page: match.Field.PageNumber));
        }

        var today = _utcNow().Date;
        if (_disallowFuture && date.Date > today)
        {
            return Task.FromResult(Result(
                ValidationStatus.Invalid,
                $"Date '{rawValue}' in field '{match.Field.Name}' is in the future.",
                extractedValue: rawValue,
                sourceDocumentId: match.Document.Id,
                page: match.Field.PageNumber));
        }

        if (_maxAge is { } maxAge && date.Date < today - maxAge)
        {
            return Task.FromResult(Result(
                ValidationStatus.Invalid,
                $"Date '{rawValue}' in field '{match.Field.Name}' is older than the allowed {maxAge.Days} days.",
                extractedValue: rawValue,
                sourceDocumentId: match.Document.Id,
                page: match.Field.PageNumber));
        }

        return Task.FromResult(Result(
            ValidationStatus.Complete,
            $"Field '{match.Field.Name}' contains a valid date.",
            extractedValue: rawValue,
            sourceDocumentId: match.Document.Id,
            page: match.Field.PageNumber));
    }

    private static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, ExplicitFormats, UnitedStatesCulture, DateTimeStyles.None, out date)
        || DateTime.TryParse(value, UnitedStatesCulture, DateTimeStyles.None, out date);
}
