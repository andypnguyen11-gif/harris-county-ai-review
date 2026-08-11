namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>A single page of an extracted document.</summary>
public sealed record ExtractedPage
{
    /// <summary>1-based page number.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Text content of the page in reading order.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Paragraph texts located on this page, in reading order.</summary>
    public IReadOnlyList<string> Paragraphs { get; init; } = [];
}
