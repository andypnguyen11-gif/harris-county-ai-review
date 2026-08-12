using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Domain;

public class BoundingBoxTests
{
    /// <summary>A 1x0.5 inch box at (2, 1) on an 8.5x11 inch page.</summary>
    private static readonly float[] Rectangle = [2f, 1f, 3f, 1f, 3f, 1.5f, 2f, 1.5f];

    [Fact]
    public void Normalizes_Polygon_Against_Page_Dimensions()
    {
        var box = BoundingBox.FromPolygon(1, Rectangle, 8.5, 11.0);

        Assert.NotNull(box);
        Assert.Equal(1, box.PageNumber);
        Assert.Equal(2d / 8.5, box.X, precision: 10);
        Assert.Equal(1d / 11.0, box.Y, precision: 10);
        Assert.Equal(1d / 8.5, box.Width, precision: 10);
        Assert.Equal(0.5d / 11.0, box.Height, precision: 10);
    }

    [Fact]
    public void Produces_Identical_Fractions_Regardless_Of_Unit()
    {
        // The same region on the same page, measured in inches and in pixels
        // at 150 DPI. Units cancel because the page is measured the same way.
        var inches = BoundingBox.FromPolygon(1, Rectangle, 8.5, 11.0);
        var pixels = BoundingBox.FromPolygon(
            1,
            [.. Rectangle.Select(value => value * 150f)],
            8.5 * 150,
            11.0 * 150);

        Assert.NotNull(inches);
        Assert.NotNull(pixels);
        Assert.Equal(inches.X, pixels.X, precision: 6);
        Assert.Equal(inches.Y, pixels.Y, precision: 6);
        Assert.Equal(inches.Width, pixels.Width, precision: 6);
        Assert.Equal(inches.Height, pixels.Height, precision: 6);
    }

    [Fact]
    public void Takes_Axis_Aligned_Bounds_Of_A_Rotated_Quadrilateral()
    {
        // A diamond: points at (2,1), (3,2), (2,3), (1,2).
        float[] diamond = [2f, 1f, 3f, 2f, 2f, 3f, 1f, 2f];

        var box = BoundingBox.FromPolygon(1, diamond, 10.0, 10.0);

        Assert.NotNull(box);
        Assert.Equal(0.1, box.X, precision: 10);
        Assert.Equal(0.1, box.Y, precision: 10);
        Assert.Equal(0.2, box.Width, precision: 10);
        Assert.Equal(0.2, box.Height, precision: 10);
    }

    [Fact]
    public void Clamps_Values_That_Fall_Outside_The_Page()
    {
        float[] overhanging = [-1f, -1f, 12f, -1f, 12f, 20f, -1f, 20f];

        var box = BoundingBox.FromPolygon(1, overhanging, 10.0, 10.0);

        Assert.NotNull(box);
        Assert.Equal(0d, box.X);
        Assert.Equal(0d, box.Y);
        Assert.Equal(1d, box.Width);
        Assert.Equal(1d, box.Height);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-5d)]
    public void Returns_Null_When_Page_Width_Is_Unusable(double? pageWidth)
    {
        Assert.Null(BoundingBox.FromPolygon(1, Rectangle, pageWidth, 11.0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-5d)]
    public void Returns_Null_When_Page_Height_Is_Unusable(double? pageHeight)
    {
        Assert.Null(BoundingBox.FromPolygon(1, Rectangle, 8.5, pageHeight));
    }

    [Fact]
    public void Returns_Null_When_Polygon_Is_Absent()
    {
        Assert.Null(BoundingBox.FromPolygon(1, null, 8.5, 11.0));
    }

    [Fact]
    public void Returns_Null_When_Polygon_Has_Fewer_Than_Four_Points()
    {
        Assert.Null(BoundingBox.FromPolygon(1, [2f, 1f, 3f, 1f, 3f, 1.5f], 8.5, 11.0));
    }

    [Fact]
    public void Returns_Null_For_A_Zero_Area_Polygon()
    {
        float[] degenerate = [2f, 1f, 2f, 1f, 2f, 1f, 2f, 1f];

        Assert.Null(BoundingBox.FromPolygon(1, degenerate, 8.5, 11.0));
    }

    [Fact]
    public void Returns_Null_When_A_Coordinate_Is_Not_Finite()
    {
        float[] broken = [2f, 1f, float.NaN, 1f, 3f, 1.5f, 2f, 1.5f];

        Assert.Null(BoundingBox.FromPolygon(1, broken, 8.5, 11.0));
    }
}
