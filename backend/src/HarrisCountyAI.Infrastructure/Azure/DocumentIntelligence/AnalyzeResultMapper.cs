using Azure.AI.DocumentIntelligence;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// Maps the Azure Document Intelligence <see cref="AnalyzeResult"/> to the
/// vendor-neutral <see cref="ExtractedDocument"/> consumed by the application
/// layer. Stateless and separately testable against SDK-shaped fixtures.
/// </summary>
public sealed class AnalyzeResultMapper
{
    public ExtractedDocument Map(Guid documentId, AnalyzeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var content = result.Content ?? string.Empty;
        var pageDimensions = MapPageDimensions(result);

        return new ExtractedDocument
        {
            DocumentId = documentId,
            Pages = MapPages(result, content),
            KeyValuePairs = MapKeyValuePairs(result, pageDimensions),
            SelectionMarks = MapSelectionMarks(result, pageDimensions),
            Tables = MapTables(result),
            RawText = content,
            ModelId = result.ModelId ?? string.Empty,
            ExtractedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Page dimensions by page number, in whatever unit the service reported.
    /// Pages that omit a dimension are absent, which makes their regions
    /// unresolvable rather than wrong.
    /// </summary>
    private static Dictionary<int, (double Width, double Height)> MapPageDimensions(AnalyzeResult result)
    {
        var dimensions = new Dictionary<int, (double Width, double Height)>();

        foreach (var page in result.Pages ?? [])
        {
            if (page.Width is { } width && page.Height is { } height)
            {
                dimensions[page.PageNumber] = (width, height);
            }
        }

        return dimensions;
    }

    /// <summary>
    /// The first region that yields a usable box. Regions whose page reported
    /// no dimensions are skipped rather than treated as failures, because a
    /// later region may still resolve.
    /// </summary>
    private static BoundingBox? ResolveBox(
        IReadOnlyList<BoundingRegion>? regions,
        IReadOnlyDictionary<int, (double Width, double Height)> pageDimensions)
    {
        foreach (var region in regions ?? [])
        {
            if (!pageDimensions.TryGetValue(region.PageNumber, out var page))
            {
                continue;
            }

            if (BoundingBox.FromPolygon(region.PageNumber, region.Polygon, page.Width, page.Height) is { } box)
            {
                return box;
            }
        }

        return null;
    }

    private static List<ExtractedPage> MapPages(AnalyzeResult result, string content)
    {
        var pages = new List<ExtractedPage>();

        foreach (var page in result.Pages ?? [])
        {
            pages.Add(new ExtractedPage
            {
                PageNumber = page.PageNumber,
                Text = GetPageText(page, content),
                Paragraphs = GetPageParagraphs(result, page.PageNumber),
            });
        }

        return pages;
    }

    /// <summary>
    /// Slices the full document content over the page's spans; falls back to
    /// joining the page's recognized lines when no spans are available.
    /// </summary>
    private static string GetPageText(DocumentPage page, string content)
    {
        var slices = (page.Spans ?? [])
            .Where(span => span.Offset >= 0 && span.Length > 0 && span.Offset + span.Length <= content.Length)
            .Select(span => content.Substring(span.Offset, span.Length))
            .ToList();

        if (slices.Count > 0)
        {
            return string.Join("\n", slices);
        }

        return string.Join("\n", (page.Lines ?? []).Select(line => line.Content ?? string.Empty));
    }

    private static List<string> GetPageParagraphs(AnalyzeResult result, int pageNumber) =>
        (result.Paragraphs ?? [])
            .Where(paragraph => (paragraph.BoundingRegions ?? []).Any(region => region.PageNumber == pageNumber))
            .Select(paragraph => paragraph.Content ?? string.Empty)
            .ToList();

    private static List<ExtractedField> MapKeyValuePairs(
        AnalyzeResult result,
        IReadOnlyDictionary<int, (double Width, double Height)> pageDimensions)
    {
        var fields = new List<ExtractedField>();

        foreach (var pair in result.KeyValuePairs ?? [])
        {
            var key = pair.Key?.Content;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            fields.Add(new ExtractedField
            {
                Key = key,
                Value = pair.Value?.Content,
                Confidence = pair.Confidence,
                PageNumber = (pair.Key!.BoundingRegions ?? []).Select(region => (int?)region.PageNumber).FirstOrDefault(),
                KeyBoundingBox = ResolveBox(pair.Key!.BoundingRegions, pageDimensions),
                ValueBoundingBox = ResolveBox(pair.Value?.BoundingRegions, pageDimensions),
            });
        }

        return fields;
    }

    private static List<ExtractedSelectionMark> MapSelectionMarks(
        AnalyzeResult result,
        IReadOnlyDictionary<int, (double Width, double Height)> pageDimensions)
    {
        var marks = new List<ExtractedSelectionMark>();

        foreach (var page in result.Pages ?? [])
        {
            var hasDimensions = pageDimensions.TryGetValue(page.PageNumber, out var dimensions);

            foreach (var mark in page.SelectionMarks ?? [])
            {
                marks.Add(new ExtractedSelectionMark
                {
                    Name = ResolveSelectionMarkName(result, mark),
                    IsSelected = mark.State == DocumentSelectionMarkState.Selected,
                    Confidence = mark.Confidence,
                    PageNumber = page.PageNumber,
                    BoundingBox = hasDimensions
                        ? BoundingBox.FromPolygon(page.PageNumber, mark.Polygon, dimensions.Width, dimensions.Height)
                        : null,
                });
            }
        }

        return marks;
    }

    /// <summary>
    /// Resolves nearby label text for a selection mark by finding a key/value
    /// pair whose value spans cover the mark's span (the service represents a
    /// checked box bound to a form field as a ":selected:"/":unselected:"
    /// value). Returns null when no such pair exists.
    /// </summary>
    private static string? ResolveSelectionMarkName(AnalyzeResult result, DocumentSelectionMark mark)
    {
        foreach (var pair in result.KeyValuePairs ?? [])
        {
            var valueSpans = pair.Value?.Spans;
            if (valueSpans is null)
            {
                continue;
            }

            if (valueSpans.Any(span => Overlaps(span, mark.Span)))
            {
                return pair.Key?.Content;
            }
        }

        return null;
    }

    private static bool Overlaps(DocumentSpan left, DocumentSpan right) =>
        left.Offset < right.Offset + right.Length && right.Offset < left.Offset + left.Length;

    private static List<ExtractedTable> MapTables(AnalyzeResult result)
    {
        var tables = new List<ExtractedTable>();

        foreach (var table in result.Tables ?? [])
        {
            tables.Add(new ExtractedTable
            {
                PageNumber = (table.BoundingRegions ?? []).Select(region => region.PageNumber).FirstOrDefault(),
                RowCount = table.RowCount,
                ColumnCount = table.ColumnCount,
                Cells = (table.Cells ?? [])
                    .Select(cell => new ExtractedTableCell
                    {
                        RowIndex = cell.RowIndex,
                        ColumnIndex = cell.ColumnIndex,
                        Content = cell.Content ?? string.Empty,
                    })
                    .ToList(),
            });
        }

        return tables;
    }
}
