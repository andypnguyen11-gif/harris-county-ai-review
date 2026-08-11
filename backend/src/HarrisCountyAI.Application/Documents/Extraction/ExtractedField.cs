namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>A key/value pair recognized on a document, e.g. a labeled form field.</summary>
public sealed record ExtractedField
{
    /// <summary>The field label as printed on the document, e.g. "Applicant Name:".</summary>
    public required string Key { get; init; }

    /// <summary>The recognized value, or null when the field was left blank.</summary>
    public string? Value { get; init; }

    /// <summary>Recognition confidence between 0 and 1, when reported.</summary>
    public double? Confidence { get; init; }

    /// <summary>1-based page number the field's key appears on, when resolvable.</summary>
    public int? PageNumber { get; init; }
}
