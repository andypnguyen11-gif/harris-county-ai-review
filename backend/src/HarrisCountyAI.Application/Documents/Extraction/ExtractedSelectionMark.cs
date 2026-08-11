namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>A checkbox or radio button recognized on a document page.</summary>
public sealed record ExtractedSelectionMark
{
    /// <summary>
    /// Nearby label text for the mark when it could be resolved (e.g. the key
    /// of the form field the mark belongs to); otherwise null.
    /// </summary>
    public string? Name { get; init; }

    public required bool IsSelected { get; init; }

    /// <summary>Recognition confidence between 0 and 1, when reported.</summary>
    public double? Confidence { get; init; }

    /// <summary>1-based page number the mark appears on.</summary>
    public required int PageNumber { get; init; }
}
