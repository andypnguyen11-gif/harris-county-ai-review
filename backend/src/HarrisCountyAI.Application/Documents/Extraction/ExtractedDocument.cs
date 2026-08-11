namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>
/// Vendor-neutral result of running a document through the extraction
/// pipeline. Downstream consumers (normalization, validation) depend on this
/// shape rather than on any specific extraction SDK.
/// </summary>
public sealed record ExtractedDocument
{
    /// <summary>Identifier of the <see cref="Domain.Entities.Document"/> the content belongs to.</summary>
    public required Guid DocumentId { get; init; }

    public IReadOnlyList<ExtractedPage> Pages { get; init; } = [];

    /// <summary>Key/value pairs recognized on forms, e.g. "Applicant Name:" → "Jane Doe".</summary>
    public IReadOnlyList<ExtractedField> KeyValuePairs { get; init; } = [];

    /// <summary>Checkboxes and radio buttons recognized on the document.</summary>
    public IReadOnlyList<ExtractedSelectionMark> SelectionMarks { get; init; } = [];

    public IReadOnlyList<ExtractedTable> Tables { get; init; } = [];

    /// <summary>Full text content of the document in reading order.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>Identifier of the analysis model that produced this result.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>UTC timestamp of when extraction completed.</summary>
    public DateTime ExtractedAt { get; init; }
}
