namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>A single cell of an <see cref="ExtractedTable"/>.</summary>
public sealed record ExtractedTableCell
{
    /// <summary>0-based row index within the table.</summary>
    public required int RowIndex { get; init; }

    /// <summary>0-based column index within the table.</summary>
    public required int ColumnIndex { get; init; }

    public string Content { get; init; } = string.Empty;
}
