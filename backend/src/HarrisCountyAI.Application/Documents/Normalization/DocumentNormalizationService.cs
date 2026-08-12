using System.Globalization;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Documents.Normalization;

/// <summary>
/// Default <see cref="IDocumentNormalizationService"/>. Maps extracted
/// key/value pairs and selection marks to <see cref="DocumentField"/>s with
/// normalized names (trimmed, lower-cased, trailing colons removed, internal
/// whitespace collapsed), classifies each field's <see cref="FieldKind"/>
/// heuristically, preserves page numbers and confidence, and joins page texts
/// into <see cref="NormalizedDocument.RawText"/>.
/// </summary>
public class DocumentNormalizationService : IDocumentNormalizationService
{
    /// <summary>Sentinels the extraction service uses for a selection-mark value bound to a form field.</summary>
    private const string SelectedSentinel = ":selected:";
    private const string UnselectedSentinel = ":unselected:";

    private static readonly string[] SignatureNameMarkers = ["signature", "signed"];

    public NormalizedDocument Normalize(Guid caseId, DocumentType documentType, ExtractedDocument extracted)
    {
        ArgumentNullException.ThrowIfNull(extracted);

        var pages = extracted.Pages
            .Select(page => new DocumentPage
            {
                Id = Guid.NewGuid(),
                PageNumber = page.PageNumber,
                Text = page.Text,
            })
            .ToList();

        var fields = new List<DocumentField>();
        fields.AddRange(extracted.KeyValuePairs.Select(NormalizeKeyValuePair).OfType<DocumentField>());
        fields.AddRange(extracted.SelectionMarks.Select(NormalizeSelectionMark));

        return new NormalizedDocument
        {
            Id = Guid.NewGuid(),
            DocumentId = extracted.DocumentId,
            CaseId = caseId,
            DocumentType = documentType,
            RawText = pages.Count > 0
                ? string.Join("\n", pages.Select(page => page.Text))
                : extracted.RawText,
            Pages = pages,
            Fields = fields,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static DocumentField? NormalizeKeyValuePair(ExtractedField pair)
    {
        var name = NormalizeFieldName(pair.Key);
        if (name.Length == 0)
        {
            return null;
        }

        var field = new DocumentField
        {
            Id = Guid.NewGuid(),
            Name = name,
            Value = pair.Value,
            Confidence = pair.Confidence,
            PageNumber = pair.PageNumber,
            KeyBoundingBox = pair.KeyBoundingBox,
            ValueBoundingBox = pair.ValueBoundingBox,
        };

        if (IsSignatureName(name))
        {
            field.Kind = FieldKind.Signature;
            field.IsSigned = HasSignatureEvidence(pair.Value);
        }
        else if (IsSelectionSentinel(pair.Value))
        {
            field.Kind = FieldKind.Checkbox;
            field.IsChecked = string.Equals(pair.Value!.Trim(), SelectedSentinel, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            field.Kind = ClassifyValue(pair.Value);
        }

        return field;
    }

    private static DocumentField NormalizeSelectionMark(ExtractedSelectionMark mark, int index)
    {
        var name = NormalizeFieldName(mark.Name);
        if (name.Length == 0)
        {
            // No nearby label could be resolved; keep the mark addressable by ordinal.
            name = $"selection mark {index + 1}";
        }

        var field = new DocumentField
        {
            Id = Guid.NewGuid(),
            Name = name,
            Value = mark.IsSelected ? SelectedSentinel : UnselectedSentinel,
            Confidence = mark.Confidence,
            PageNumber = mark.PageNumber,
            ValueBoundingBox = mark.BoundingBox,
        };

        if (IsSignatureName(name))
        {
            field.Kind = FieldKind.Signature;
            field.IsSigned = mark.IsSelected;
        }
        else
        {
            field.Kind = FieldKind.Checkbox;
            field.IsChecked = mark.IsSelected;
        }

        return field;
    }

    /// <summary>Trims whitespace and trailing colons, collapses internal whitespace, and lower-cases.</summary>
    internal static string NormalizeFieldName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.TrimEnd(':', ' ').ToLowerInvariant();
    }

    private static bool IsSignatureName(string normalizedName) =>
        SignatureNameMarkers.Any(normalizedName.Contains);

    /// <summary>A signature field counts as signed when it has a value beyond an unselected mark.</summary>
    private static bool HasSignatureEvidence(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value.Trim(), UnselectedSentinel, StringComparison.OrdinalIgnoreCase);

    private static bool IsSelectionSentinel(string? value)
    {
        var trimmed = value?.Trim();
        return string.Equals(trimmed, SelectedSentinel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, UnselectedSentinel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Classifies a plain value: numeric before date so purely numeric values
    /// (e.g. "42", "3.14") are not mistaken for dates, then date-parseable
    /// values, then free text.
    /// </summary>
    private static FieldKind ClassifyValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FieldKind.Text;
        }

        var trimmed = value.Trim();

        if (double.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return FieldKind.Number;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return FieldKind.Date;
        }

        return FieldKind.Text;
    }
}
