namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>A table recognized on a document.</summary>
public sealed record ExtractedTable
{
    /// <summary>1-based page number the table starts on, or 0 when unknown.</summary>
    public required int PageNumber { get; init; }

    public required int RowCount { get; init; }

    public required int ColumnCount { get; init; }

    public IReadOnlyList<ExtractedTableCell> Cells { get; init; } = [];
}
