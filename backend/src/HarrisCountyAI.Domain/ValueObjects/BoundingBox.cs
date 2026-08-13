namespace HarrisCountyAI.Domain.ValueObjects;

/// <summary>
/// An axis-aligned region of a single document page, expressed as fractions of
/// the page's width and height with the origin at the top-left corner.
/// </summary>
/// <remarks>
/// The page number travels with the region rather than beside it. Which of a
/// field's two regions a validation rule reports is decided by the rule, so a
/// page number kept in a separate property would have to be matched to that
/// choice by convention; carrying it here makes the pairing impossible to get
/// wrong.
/// </remarks>
public sealed record BoundingBox
{
    /// <summary>1-based page the region lies on.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Distance from the left edge, as a fraction of page width.</summary>
    public required double X { get; init; }

    /// <summary>Distance from the top edge, as a fraction of page height.</summary>
    public required double Y { get; init; }

    /// <summary>Width as a fraction of page width.</summary>
    public required double Width { get; init; }

    /// <summary>Height as a fraction of page height.</summary>
    public required double Height { get; init; }

    /// <summary>
    /// Converts a recognition polygon into a normalized region, or returns null
    /// when the polygon or the page dimensions cannot describe a real area.
    /// </summary>
    /// <param name="pageNumber">1-based page the polygon was reported on.</param>
    /// <param name="polygon">
    /// Flattened point pairs — four points, eight values — in the same unit of
    /// measure as <paramref name="pageWidth"/> and <paramref name="pageHeight"/>.
    /// The unit itself is irrelevant: dividing by the page's own dimensions
    /// cancels it, so inches and pixels yield identical fractions.
    /// </param>
    /// <param name="pageWidth">Page width in the polygon's unit of measure.</param>
    /// <param name="pageHeight">Page height in the polygon's unit of measure.</param>
    public static BoundingBox? FromPolygon(
        int pageNumber,
        IReadOnlyList<float>? polygon,
        double? pageWidth,
        double? pageHeight)
    {
        if (polygon is null || polygon.Count < 8)
        {
            return null;
        }

        if (pageWidth is not > 0 || pageHeight is not > 0
            || !double.IsFinite(pageWidth.Value) || !double.IsFinite(pageHeight.Value))
        {
            return null;
        }

        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;

        for (var index = 0; index + 1 < polygon.Count; index += 2)
        {
            double x = polygon[index];
            double y = polygon[index + 1];

            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                return null;
            }

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        var left = Fraction(minX, pageWidth.Value);
        var right = Fraction(maxX, pageWidth.Value);
        var top = Fraction(minY, pageHeight.Value);
        var bottom = Fraction(maxY, pageHeight.Value);

        var width = right - left;
        var height = bottom - top;

        // A region with no area cannot be drawn and tells a reviewer nothing;
        // reporting no region at all is the honest answer.
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new BoundingBox
        {
            PageNumber = pageNumber,
            X = left,
            Y = top,
            Width = width,
            Height = height,
        };
    }

    private static double Fraction(double value, double pageLength) =>
        Math.Clamp(value / pageLength, 0d, 1d);
}
