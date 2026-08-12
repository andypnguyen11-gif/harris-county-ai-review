# PR-A: Document Region Coordinates (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry Azure Document Intelligence bounding polygons from the extraction response through to `ValidationReportDto`, so a later PR can draw a box over the region a validation finding came from.

**Architecture:** Azure DI already returns a bounding polygon on every key, value, and selection mark; `AnalyzeResultMapper` reads those regions today only to pull a page number and discards the polygon. This PR introduces a `BoundingBox` value object normalized to 0–1 page fractions at map time, threads two boxes (key and value) through `ExtractedField` → `DocumentField`, has each validation rule resolve them to a single box on its result, and persists that box on `ValidationReportItem`.

**Tech Stack:** .NET 10, C#, EF Core (SQL Server), `Azure.AI.DocumentIntelligence` 1.0.0, xUnit.

**Spec:** `docs/architecture/document-region-highlighting.md`

## Global Constraints

- **Backend only.** This PR touches no file under `frontend/`. The TypeScript mirror in `validation.model.ts` belongs to PR-B.
- **Clean Architecture layering holds.** `Domain` takes no external dependencies — `BoundingBox` uses only BCL types. `ArchitectureTests` enforce this.
- **Normalization happens once, at the mapper.** Values crossing any layer boundary are already fractions in `[0, 1]`, origin top-left. No layer downstream converts units.
- **Degenerate input yields `null`, never a zero-size box.**
- **No invented regions.** A box exists only where OCR reported a polygon.
- **Every new or changed behavior gets a test.** `dotnet build` and `dotnet test` must pass before the PR is done (per `CLAUDE.md`).
- **Commit messages describe the change and never reference PR or task numbers.** No AI attribution of any kind — no `Co-Authored-By`, no "Generated with" line.
- **Branch:** `feature/pr-XX-document-region-coordinates` (substitute the real PR number when it is assigned in `Tasks.md`).
- **All new columns are nullable.** Documents normalized before this change keep null boxes; there is no backfill job.

### Verified SDK facts (do not re-derive)

Confirmed by reflection against `Azure.AI.DocumentIntelligence` 1.0.0:

| Member | Type |
|---|---|
| `BoundingRegion.PageNumber` | `int` |
| `BoundingRegion.Polygon` | `IReadOnlyList<float>` |
| `DocumentPage.Width` / `.Height` | `float?` |
| `DocumentPage.Unit` | `LengthUnit?` (**never read** — units cancel) |
| `LengthUnit` | extensible-enum **struct**, members `LengthUnit.Inch` / `LengthUnit.Pixel` |
| `DocumentSelectionMarkState` | extensible-enum **struct**, members `.Selected` / `.Unselected` |
| `DocumentSelectionMark.Polygon` | `IReadOnlyList<float>` |
| `DocumentKeyValueElement.BoundingRegions` | `IReadOnlyList<BoundingRegion>` |
| `DocumentKeyValuePair.Confidence` | `float` |

Test fixture factory signatures (all parameters optional, use named arguments):

```csharp
DocumentIntelligenceModelFactory.BoundingRegion(int pageNumber, IEnumerable<float> polygon)
DocumentIntelligenceModelFactory.DocumentPage(int pageNumber, float? angle, float? width, float? height, LengthUnit? unit, IEnumerable<DocumentSpan> spans, IEnumerable<DocumentWord> words, IEnumerable<DocumentSelectionMark> selectionMarks, IEnumerable<DocumentLine> lines, IEnumerable<DocumentBarcode> barcodes, IEnumerable<DocumentFormula> formulas)
DocumentIntelligenceModelFactory.DocumentKeyValueElement(string content, IEnumerable<BoundingRegion> boundingRegions, IEnumerable<DocumentSpan> spans)
DocumentIntelligenceModelFactory.DocumentKeyValuePair(DocumentKeyValueElement key, DocumentKeyValueElement value, float confidence)
DocumentIntelligenceModelFactory.DocumentSelectionMark(DocumentSelectionMarkState state, IEnumerable<float> polygon, DocumentSpan span, float confidence)
DocumentIntelligenceModelFactory.DocumentSpan(int offset, int length)
```

A polygon is **eight floats** — four points, clockwise from top-left relative to text orientation: `[x1,y1, x2,y2, x3,y3, x4,y4]`.

---

## File Structure

**Create**

| File | Responsibility |
|---|---|
| `backend/src/HarrisCountyAI.Domain/ValueObjects/BoundingBox.cs` | The value object and the polygon→normalized-rect transform, with its invariants |
| `backend/tests/HarrisCountyAI.UnitTests/Domain/BoundingBoxTests.cs` | Transform and invariant tests |

**Modify**

| File | Change |
|---|---|
| `.../Application/Documents/Extraction/ExtractedField.cs` | `KeyBoundingBox`, `ValueBoundingBox` |
| `.../Application/Documents/Extraction/ExtractedSelectionMark.cs` | `BoundingBox` |
| `.../Infrastructure/Azure/DocumentIntelligence/AnalyzeResultMapper.cs` | Keep polygons; page-dimension lookup |
| `.../Domain/Entities/DocumentField.cs` | `KeyBoundingBox`, `ValueBoundingBox` |
| `.../Application/Documents/Normalization/DocumentNormalizationService.cs` | Thread boxes through |
| `.../Domain/Validation/ValidationResult.cs` | `BoundingBox` |
| `.../Application/Validation/Rules/ValidationRuleBase.cs` | `boundingBox` parameter on `Result` |
| `.../Domain/Entities/ValidationReportItem.cs` | `BoundingBox` |
| `.../Application/Validation/RunValidation/ValidationReportFactory.cs` | Copy box onto the item |
| `.../Application/Validation/Rules/RequiredFieldRule.cs` | Resolve box |
| `.../Application/Validation/Rules/DateRule.cs` | Resolve box |
| `.../Application/Validation/Rules/SignatureRule.cs` | Resolve box |
| `.../Application/Validation/Rules/CheckboxRule.cs` | Resolve box **+ report document/page on the unchecked branch** |
| `.../Infrastructure/Persistence/Configurations/NormalizedDocumentConfiguration.cs` | Owned box mapping |
| `.../Infrastructure/Persistence/Configurations/ValidationReportConfiguration.cs` | Owned box mapping |
| `.../Application/Validation/ValidationReportDto.cs` | Expose the box |
| `backend/tests/HarrisCountyAI.UnitTests/Validation/NormalizedDocumentBuilder.cs` | Optional box arguments |

---

### Task 1: `BoundingBox` value object

**Files:**
- Create: `backend/src/HarrisCountyAI.Domain/ValueObjects/BoundingBox.cs`
- Test: `backend/tests/HarrisCountyAI.UnitTests/Domain/BoundingBoxTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `HarrisCountyAI.Domain.ValueObjects.BoundingBox`, a `sealed record` with `int PageNumber`, `double X`, `double Y`, `double Width`, `double Height`, all `required`; and the static factory
  `BoundingBox? FromPolygon(int pageNumber, IReadOnlyList<float>? polygon, double? pageWidth, double? pageHeight)`.

`Domain/ValueObjects/` is a new folder. `ArchitectureTests` enforce layer dependencies, not folder names, so no architecture rule changes.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/HarrisCountyAI.UnitTests/Domain/BoundingBoxTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~BoundingBoxTests"`
Expected: FAIL — the build breaks because `HarrisCountyAI.Domain.ValueObjects.BoundingBox` does not exist.

- [ ] **Step 3: Write the implementation**

Create `backend/src/HarrisCountyAI.Domain/ValueObjects/BoundingBox.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~BoundingBoxTests"`
Expected: PASS, 14 tests (the two `[Theory]` cases expand to three each).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HarrisCountyAI.Domain/ValueObjects/BoundingBox.cs \
        backend/tests/HarrisCountyAI.UnitTests/Domain/BoundingBoxTests.cs
git commit -m "Add a normalized bounding box value object"
```

---

### Task 2: Keep polygons in the extraction mapper

**Files:**
- Modify: `backend/src/HarrisCountyAI.Application/Documents/Extraction/ExtractedField.cs`
- Modify: `backend/src/HarrisCountyAI.Application/Documents/Extraction/ExtractedSelectionMark.cs`
- Modify: `backend/src/HarrisCountyAI.Infrastructure/Azure/DocumentIntelligence/AnalyzeResultMapper.cs`
- Test: `backend/tests/HarrisCountyAI.UnitTests/DocumentIntelligence/AnalyzeResultMapperTests.cs`

**Interfaces:**
- Consumes: `BoundingBox.FromPolygon(int, IReadOnlyList<float>?, double?, double?)` from Task 1.
- Produces: `ExtractedField.KeyBoundingBox` and `.ValueBoundingBox` (both `BoundingBox?`); `ExtractedSelectionMark.BoundingBox` (`BoundingBox?`).

`ExtractedField.PageNumber` keeps its current meaning — the first key region's page — and is **not** changed by this task.

- [ ] **Step 1: Write the failing tests**

Append to `backend/tests/HarrisCountyAI.UnitTests/DocumentIntelligence/AnalyzeResultMapperTests.cs` (inside the existing class; the file already has `using Azure.AI.DocumentIntelligence;`, add `using HarrisCountyAI.Domain.ValueObjects;` if the analyzer asks for it):

```csharp
    private static DocumentPage LetterPage(int pageNumber, IEnumerable<DocumentSelectionMark>? selectionMarks = null) =>
        DocumentIntelligenceModelFactory.DocumentPage(
            pageNumber: pageNumber,
            width: 8.5f,
            height: 11f,
            unit: LengthUnit.Inch,
            selectionMarks: selectionMarks);

    private static DocumentKeyValueElement Element(string content, int pageNumber, float[] polygon) =>
        DocumentIntelligenceModelFactory.DocumentKeyValueElement(
            content: content,
            boundingRegions:
            [
                DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: pageNumber, polygon: polygon),
            ]);

    [Fact]
    public void Maps_Key_And_Value_Polygons_To_Normalized_Boxes()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [LetterPage(1)],
            keyValuePairs:
            [
                DocumentIntelligenceModelFactory.DocumentKeyValuePair(
                    key: Element("Owner Name:", 1, [1f, 2f, 3f, 2f, 3f, 2.5f, 1f, 2.5f]),
                    value: Element("Trenton Okafor", 1, [3.5f, 2f, 6f, 2f, 6f, 2.5f, 3.5f, 2.5f]),
                    confidence: 0.9f),
            ]);

        var field = _mapper.Map(DocumentId, result).KeyValuePairs.Single();

        Assert.NotNull(field.KeyBoundingBox);
        Assert.Equal(1, field.KeyBoundingBox.PageNumber);
        Assert.Equal(1d / 8.5, field.KeyBoundingBox.X, precision: 6);
        Assert.Equal(2d / 11d, field.KeyBoundingBox.Y, precision: 6);

        Assert.NotNull(field.ValueBoundingBox);
        Assert.Equal(3.5d / 8.5, field.ValueBoundingBox.X, precision: 6);
    }

    [Fact]
    public void Leaves_Boxes_Null_When_The_Page_Reports_No_Dimensions()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            keyValuePairs:
            [
                DocumentIntelligenceModelFactory.DocumentKeyValuePair(
                    key: Element("Owner Name:", 1, [1f, 2f, 3f, 2f, 3f, 2.5f, 1f, 2.5f]),
                    value: null,
                    confidence: 0.9f),
            ]);

        var field = _mapper.Map(DocumentId, result).KeyValuePairs.Single();

        Assert.Null(field.KeyBoundingBox);
        Assert.Null(field.ValueBoundingBox);
        // The page number still resolves from the region, as it did before.
        Assert.Equal(1, field.PageNumber);
    }

    [Fact]
    public void Leaves_Value_Box_Null_When_The_Field_Is_Blank()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [LetterPage(1)],
            keyValuePairs:
            [
                DocumentIntelligenceModelFactory.DocumentKeyValuePair(
                    key: Element("HCAD Account Number:", 1, [1f, 3f, 4f, 3f, 4f, 3.5f, 1f, 3.5f]),
                    value: null,
                    confidence: 0.8f),
            ]);

        var field = _mapper.Map(DocumentId, result).KeyValuePairs.Single();

        Assert.NotNull(field.KeyBoundingBox);
        Assert.Null(field.ValueBoundingBox);
    }

    [Fact]
    public void Maps_Selection_Mark_Polygon_To_A_Normalized_Box()
    {
        var mark = DocumentIntelligenceModelFactory.DocumentSelectionMark(
            state: DocumentSelectionMarkState.Selected,
            polygon: [1f, 5f, 1.2f, 5f, 1.2f, 5.2f, 1f, 5.2f],
            span: DocumentIntelligenceModelFactory.DocumentSpan(0, 10),
            confidence: 0.95f);

        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [LetterPage(2, selectionMarks: [mark])]);

        var extractedMark = _mapper.Map(DocumentId, result).SelectionMarks.Single();

        Assert.NotNull(extractedMark.BoundingBox);
        Assert.Equal(2, extractedMark.BoundingBox.PageNumber);
        Assert.Equal(1d / 8.5, extractedMark.BoundingBox.X, precision: 6);
        Assert.Equal(5d / 11d, extractedMark.BoundingBox.Y, precision: 6);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~AnalyzeResultMapperTests"`
Expected: FAIL — `ExtractedField` has no `KeyBoundingBox`, `ExtractedSelectionMark` has no `BoundingBox`.

- [ ] **Step 3: Add the properties to the extraction contracts**

In `ExtractedField.cs`, add `using HarrisCountyAI.Domain.ValueObjects;` and append:

```csharp
    /// <summary>Region of the field's printed label, when it was located.</summary>
    public BoundingBox? KeyBoundingBox { get; init; }

    /// <summary>Region of the field's recognized value, when it was located.</summary>
    public BoundingBox? ValueBoundingBox { get; init; }
```

In `ExtractedSelectionMark.cs`, add `using HarrisCountyAI.Domain.ValueObjects;` and append:

```csharp
    /// <summary>Region of the mark itself, when it was located.</summary>
    public BoundingBox? BoundingBox { get; init; }
```

- [ ] **Step 4: Populate them in the mapper**

In `AnalyzeResultMapper.cs`, add `using HarrisCountyAI.Domain.ValueObjects;`.

Add a page-dimension lookup and a region resolver:

```csharp
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
```

Change `Map` to build the lookup once and pass it down:

```csharp
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
```

In `MapKeyValuePairs`, change the signature to accept the lookup and populate both boxes. `PageNumber` keeps its existing derivation:

```csharp
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
```

In `MapSelectionMarks`, take the lookup and resolve the mark's own polygon against its page:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~AnalyzeResultMapperTests"`
Expected: PASS — the four new tests plus every pre-existing mapper test.

- [ ] **Step 6: Commit**

```bash
git add backend/src/HarrisCountyAI.Application/Documents/Extraction/ExtractedField.cs \
        backend/src/HarrisCountyAI.Application/Documents/Extraction/ExtractedSelectionMark.cs \
        backend/src/HarrisCountyAI.Infrastructure/Azure/DocumentIntelligence/AnalyzeResultMapper.cs \
        backend/tests/HarrisCountyAI.UnitTests/DocumentIntelligence/AnalyzeResultMapperTests.cs
git commit -m "Keep recognition polygons as normalized regions during extraction"
```

---

### Task 3: Carry regions through normalization

**Files:**
- Modify: `backend/src/HarrisCountyAI.Domain/Entities/DocumentField.cs`
- Modify: `backend/src/HarrisCountyAI.Application/Documents/Normalization/DocumentNormalizationService.cs`
- Test: `backend/tests/HarrisCountyAI.UnitTests/Documents/` (add `DocumentNormalizationBoundingBoxTests.cs`; if a normalization test file already exists in that folder, append to it instead)

**Interfaces:**
- Consumes: `ExtractedField.KeyBoundingBox` / `.ValueBoundingBox`, `ExtractedSelectionMark.BoundingBox` from Task 2.
- Produces: `DocumentField.KeyBoundingBox` and `.ValueBoundingBox` (both `BoundingBox?`).

**Locked decision:** a selection mark has one polygon and no key/value split. It lands on **`ValueBoundingBox`**, with `KeyBoundingBox` left null. `FieldKind` already distinguishes checkbox and signature fields, so nothing is lost, and either resolution order (`Value ?? Key` or `Key ?? Value`) finds the mark.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/HarrisCountyAI.UnitTests/Documents/DocumentNormalizationBoundingBoxTests.cs`:

```csharp
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Documents;

public class DocumentNormalizationBoundingBoxTests
{
    private readonly DocumentNormalizationService _service = new();

    private static BoundingBox Box(int pageNumber, double x) => new()
    {
        PageNumber = pageNumber,
        X = x,
        Y = 0.2,
        Width = 0.1,
        Height = 0.02,
    };

    private static ExtractedDocument DocumentWith(
        IReadOnlyList<ExtractedField>? keyValuePairs = null,
        IReadOnlyList<ExtractedSelectionMark>? selectionMarks = null) => new()
    {
        DocumentId = Guid.NewGuid(),
        Pages = [],
        KeyValuePairs = keyValuePairs ?? [],
        SelectionMarks = selectionMarks ?? [],
        Tables = [],
        RawText = string.Empty,
        ModelId = "prebuilt-layout",
        ExtractedAt = DateTime.UtcNow,
    };

    [Fact]
    public void Carries_Both_Regions_Onto_The_Normalized_Field()
    {
        var extracted = DocumentWith(keyValuePairs:
        [
            new ExtractedField
            {
                Key = "Owner Name:",
                Value = "Trenton Okafor",
                PageNumber = 1,
                KeyBoundingBox = Box(1, 0.1),
                ValueBoundingBox = Box(1, 0.4),
            },
        ]);

        var field = _service
            .Normalize(Guid.NewGuid(), DocumentType.PermitApplication, extracted)
            .Fields.Single();

        Assert.Equal(Box(1, 0.1), field.KeyBoundingBox);
        Assert.Equal(Box(1, 0.4), field.ValueBoundingBox);
    }

    [Fact]
    public void Leaves_Regions_Null_When_Extraction_Reported_None()
    {
        var extracted = DocumentWith(keyValuePairs:
        [
            new ExtractedField { Key = "Owner Name:", Value = "Trenton Okafor", PageNumber = 1 },
        ]);

        var field = _service
            .Normalize(Guid.NewGuid(), DocumentType.PermitApplication, extracted)
            .Fields.Single();

        Assert.Null(field.KeyBoundingBox);
        Assert.Null(field.ValueBoundingBox);
    }

    [Fact]
    public void Puts_A_Selection_Marks_Region_On_The_Value_Box()
    {
        var extracted = DocumentWith(selectionMarks:
        [
            new ExtractedSelectionMark
            {
                Name = "Accessory Building",
                IsSelected = true,
                PageNumber = 1,
                BoundingBox = Box(1, 0.05),
            },
        ]);

        var field = _service
            .Normalize(Guid.NewGuid(), DocumentType.PermitApplication, extracted)
            .Fields.Single();

        Assert.Equal(FieldKind.Checkbox, field.Kind);
        Assert.Equal(Box(1, 0.05), field.ValueBoundingBox);
        Assert.Null(field.KeyBoundingBox);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~DocumentNormalizationBoundingBoxTests"`
Expected: FAIL — `DocumentField` has no `KeyBoundingBox`.

- [ ] **Step 3: Add the properties to `DocumentField`**

In `DocumentField.cs`, add `using HarrisCountyAI.Domain.ValueObjects;` and append:

```csharp
    /// <summary>Region of the field's printed label, when it was located.</summary>
    public BoundingBox? KeyBoundingBox { get; set; }

    /// <summary>
    /// Region of the field's recognized value, when it was located. For a
    /// checkbox or signature recognized as a selection mark this holds the
    /// mark's own region, and <see cref="KeyBoundingBox"/> stays null.
    /// </summary>
    public BoundingBox? ValueBoundingBox { get; set; }
```

- [ ] **Step 4: Thread them through the normalization service**

In `DocumentNormalizationService.NormalizeKeyValuePair`, extend the object initializer:

```csharp
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
```

In `NormalizeSelectionMark`, extend the object initializer:

```csharp
        var field = new DocumentField
        {
            Id = Guid.NewGuid(),
            Name = name,
            Value = mark.IsSelected ? SelectedSentinel : UnselectedSentinel,
            Confidence = mark.Confidence,
            PageNumber = mark.PageNumber,
            ValueBoundingBox = mark.BoundingBox,
        };
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~DocumentNormalization"`
Expected: PASS — the three new tests plus every pre-existing normalization test.

- [ ] **Step 6: Commit**

```bash
git add backend/src/HarrisCountyAI.Domain/Entities/DocumentField.cs \
        backend/src/HarrisCountyAI.Application/Documents/Normalization/DocumentNormalizationService.cs \
        backend/tests/HarrisCountyAI.UnitTests/Documents/DocumentNormalizationBoundingBoxTests.cs
git commit -m "Carry document regions through field normalization"
```

---

### Task 4: Plumb a region through validation results

**Files:**
- Modify: `backend/src/HarrisCountyAI.Domain/Validation/ValidationResult.cs`
- Modify: `backend/src/HarrisCountyAI.Application/Validation/Rules/ValidationRuleBase.cs`
- Modify: `backend/src/HarrisCountyAI.Domain/Entities/ValidationReportItem.cs`
- Modify: `backend/src/HarrisCountyAI.Application/Validation/RunValidation/ValidationReportFactory.cs`
- Test: `backend/tests/HarrisCountyAI.UnitTests/Validation/Reports/` (add `ValidationReportFactoryBoundingBoxTests.cs`)

**Interfaces:**
- Consumes: `BoundingBox` from Task 1.
- Produces: `ValidationResult.BoundingBox` (`BoundingBox?`); an optional `BoundingBox? boundingBox = null` trailing parameter on `ValidationRuleBase.Result`; `ValidationReportItem.BoundingBox` (`BoundingBox?`).

No rule sets a box yet — Tasks 5 and 6 do that. This task delivers the pipe and proves it carries.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/HarrisCountyAI.UnitTests/Validation/Reports/ValidationReportFactoryBoundingBoxTests.cs`:

```csharp
using HarrisCountyAI.Application.Validation.RunValidation;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

public class ValidationReportFactoryBoundingBoxTests
{
    private static readonly BoundingBox Region = new()
    {
        PageNumber = 2,
        X = 0.1,
        Y = 0.2,
        Width = 0.3,
        Height = 0.04,
    };

    private static ValidationResult ResultWith(BoundingBox? box, Guid? sourceDocumentId) => new()
    {
        Requirement = "HCAD account number",
        Status = ValidationStatus.Missing,
        Message = "Field 'hcad account number' is present but has no value.",
        ValidationType = ValidationType.Deterministic,
        RuleName = "RequiredFieldRule(HCAD account number)",
        SourceDocumentId = sourceDocumentId,
        Page = box?.PageNumber,
        BoundingBox = box,
    };

    [Fact]
    public void Copies_The_Region_Onto_The_Report_Item()
    {
        var report = ValidationReportFactory.Create(
            Guid.NewGuid(),
            WorkflowType.FloodplainDevelopmentPermit,
            [ResultWith(Region, sourceDocumentId: null)],
            []);

        Assert.Equal(Region, report.Items.Single().BoundingBox);
    }

    [Fact]
    public void Leaves_The_Region_Null_When_The_Result_Has_None()
    {
        var report = ValidationReportFactory.Create(
            Guid.NewGuid(),
            WorkflowType.FloodplainDevelopmentPermit,
            [ResultWith(box: null, sourceDocumentId: null)],
            []);

        Assert.Null(report.Items.Single().BoundingBox);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~ValidationReportFactoryBoundingBoxTests"`
Expected: FAIL — `ValidationResult` has no `BoundingBox`.

- [ ] **Step 3: Add the property to `ValidationResult`**

In `ValidationResult.cs`, add `using HarrisCountyAI.Domain.ValueObjects;` and append:

```csharp
    /// <summary>
    /// Region of the source document the evidence was read from, when it could
    /// be located. Null means no region was reported — the finding cannot be
    /// pointed at on the page, and must say so rather than guess.
    /// </summary>
    public BoundingBox? BoundingBox { get; init; }
```

- [ ] **Step 4: Add the parameter to `ValidationRuleBase.Result`**

In `ValidationRuleBase.cs`, add `using HarrisCountyAI.Domain.ValueObjects;` and extend the helper. The new parameter is optional and trailing, so every existing call site keeps compiling unchanged:

```csharp
    protected ValidationResult Result(
        ValidationStatus status,
        string message,
        string? extractedValue = null,
        Guid? sourceDocumentId = null,
        int? page = null,
        BoundingBox? boundingBox = null) =>
        new()
        {
            Requirement = Requirement,
            Status = status,
            Message = message,
            ExtractedValue = extractedValue,
            SourceDocumentId = sourceDocumentId,
            Page = page,
            BoundingBox = boundingBox,
            ValidationType = ValidationType.Deterministic,
            RuleName = Name,
        };
```

- [ ] **Step 5: Add the property to `ValidationReportItem`**

In `ValidationReportItem.cs`, add `using HarrisCountyAI.Domain.ValueObjects;` and append:

```csharp
    /// <summary>
    /// Region of the source document the evidence was read from, when it could
    /// be located. Null when no region was reported.
    /// </summary>
    public BoundingBox? BoundingBox { get; set; }
```

- [ ] **Step 6: Copy it in `ValidationReportFactory`**

In `CreateItem`, add one line to the object initializer, after `PageNumber`:

```csharp
            PageNumber = result.Page,
            BoundingBox = result.BoundingBox,
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~ValidationReportFactory"`
Expected: PASS — the two new tests plus every pre-existing factory test.

- [ ] **Step 8: Commit**

```bash
git add backend/src/HarrisCountyAI.Domain/Validation/ValidationResult.cs \
        backend/src/HarrisCountyAI.Application/Validation/Rules/ValidationRuleBase.cs \
        backend/src/HarrisCountyAI.Domain/Entities/ValidationReportItem.cs \
        backend/src/HarrisCountyAI.Application/Validation/RunValidation/ValidationReportFactory.cs \
        backend/tests/HarrisCountyAI.UnitTests/Validation/Reports/ValidationReportFactoryBoundingBoxTests.cs
git commit -m "Carry an evidence region on validation results and report items"
```

---

### Task 5: Resolve regions in the field and date rules

**Files:**
- Modify: `backend/tests/HarrisCountyAI.UnitTests/Validation/NormalizedDocumentBuilder.cs`
- Modify: `backend/src/HarrisCountyAI.Application/Validation/Rules/RequiredFieldRule.cs`
- Modify: `backend/src/HarrisCountyAI.Application/Validation/Rules/DateRule.cs`
- Test: `backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/RequiredFieldRuleTests.cs`, `.../DateRuleTests.cs`

**Interfaces:**
- Consumes: `DocumentField.KeyBoundingBox` / `.ValueBoundingBox` (Task 3); `ValidationRuleBase.Result(..., BoundingBox? boundingBox)` (Task 4).
- Produces: `NormalizedDocumentBuilder.WithTextField(..., BoundingBox? keyBox, BoundingBox? valueBox)` and the same two optional trailing parameters on `WithDateField`, `WithNumberField`, `WithCheckbox`, `WithSignature`.

**The resolution rule, and why the two orders differ:**

| Situation | Box |
|---|---|
| a value is present on the page | `ValueBoundingBox ?? KeyBoundingBox` |
| the field is present but empty | `KeyBoundingBox ?? ValueBoundingBox` |
| the field was never found | `null` |

The empty case prefers the **key** deliberately: the printed label is what a reviewer scans for, and a blank field's value region is either absent or too small to see.

The reported page is read off the chosen box so the two cannot disagree:
`page: box?.PageNumber ?? match.Field.PageNumber`.

- [ ] **Step 1: Add optional region arguments to the test builder**

In `NormalizedDocumentBuilder.cs`, add `using HarrisCountyAI.Domain.ValueObjects;`, then extend `AddField` and each `With...` method with two optional trailing parameters. `AddField` becomes:

```csharp
    private void AddField(
        string name,
        FieldKind kind,
        string? value = null,
        bool? isChecked = null,
        bool? isSigned = null,
        int? page = 1,
        BoundingBox? keyBox = null,
        BoundingBox? valueBox = null)
    {
        _document.Fields.Add(new DocumentField
        {
            Id = Guid.NewGuid(),
            Name = name,
            Value = value,
            Kind = kind,
            IsChecked = isChecked,
            IsSigned = isSigned,
            Confidence = 0.95,
            PageNumber = page,
            KeyBoundingBox = keyBox,
            ValueBoundingBox = valueBox,
        });
    }
```

And each public method forwards them, for example:

```csharp
    public NormalizedDocumentBuilder WithTextField(
        string name,
        string? value,
        int? page = 1,
        BoundingBox? keyBox = null,
        BoundingBox? valueBox = null)
    {
        AddField(name, FieldKind.Text, value: value, page: page, keyBox: keyBox, valueBox: valueBox);
        return this;
    }
```

Apply the identical treatment to `WithDateField`, `WithNumberField`, `WithCheckbox`, and `WithSignature`. Existing call sites are unaffected because the parameters are optional and trailing.

- [ ] **Step 2: Write the failing tests**

Append to `backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/RequiredFieldRuleTests.cs` (add `using HarrisCountyAI.Domain.ValueObjects;`):

```csharp
    private static BoundingBox RegionOn(int pageNumber, double x) => new()
    {
        PageNumber = pageNumber,
        X = x,
        Y = 0.3,
        Width = 0.2,
        Height = 0.03,
    };

    [Fact]
    public async Task Reports_The_Value_Region_When_The_Field_Has_A_Value()
    {
        var keyBox = RegionOn(1, 0.1);
        var valueBox = RegionOn(1, 0.5);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor", page: 1, keyBox: keyBox, valueBox: valueBox)
            .Build();
        var rule = new RequiredFieldRule("Owner name", "owner name");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(valueBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Key_Region_When_The_Field_Is_Present_But_Empty()
    {
        var keyBox = RegionOn(1, 0.1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("hcad account number", null, page: 1, keyBox: keyBox)
            .Build();
        var rule = new RequiredFieldRule("HCAD account number", "hcad account number");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(keyBox, result.BoundingBox);
    }

    [Fact]
    public async Task Falls_Back_To_The_Key_Region_When_There_Is_No_Value_Region()
    {
        var keyBox = RegionOn(1, 0.1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor", page: 1, keyBox: keyBox)
            .Build();
        var rule = new RequiredFieldRule("Owner name", "owner name");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(keyBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_No_Region_When_The_Field_Was_Never_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor")
            .Build();
        var rule = new RequiredFieldRule("Block", "block");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Null(result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Page_Of_The_Region_It_Chose()
    {
        // The label sits on page 1 and the value wraps onto page 2. The
        // reported page must follow the box that was reported.
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField(
                "project description",
                "Placing fill across the rear third of the lot",
                page: 1,
                keyBox: RegionOn(1, 0.1),
                valueBox: RegionOn(2, 0.1))
            .Build();
        var rule = new RequiredFieldRule("Project description", "project description");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(2, result.BoundingBox?.PageNumber);
        Assert.Equal(2, result.Page);
    }
```

Append to `backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/DateRuleTests.cs` (add the same `using` and a local copy of the `RegionOn` helper — do not share it across test classes):

```csharp
    private static BoundingBox RegionOn(int pageNumber, double x) => new()
    {
        PageNumber = pageNumber,
        X = x,
        Y = 0.3,
        Width = 0.2,
        Height = 0.03,
    };

    [Fact]
    public async Task Reports_The_Value_Region_For_A_Valid_Date()
    {
        var valueBox = RegionOn(2, 0.5);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("application date", "1/15/2026", page: 2, keyBox: RegionOn(2, 0.1), valueBox: valueBox)
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(valueBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Value_Region_For_An_Unparseable_Date()
    {
        var valueBox = RegionOn(2, 0.5);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("application date", "not a date", page: 2, keyBox: RegionOn(2, 0.1), valueBox: valueBox)
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Equal(valueBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Key_Region_For_An_Empty_Date()
    {
        var keyBox = RegionOn(2, 0.1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("application date", null, page: 2, keyBox: keyBox)
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(keyBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_No_Region_When_The_Date_Field_Was_Never_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor")
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Null(result.BoundingBox);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~RequiredFieldRuleTests|FullyQualifiedName~DateRuleTests"`
Expected: FAIL — every new assertion on `result.BoundingBox` gets null.

- [ ] **Step 4: Resolve the region in `RequiredFieldRule`**

In `RequiredFieldRule.cs`, add `using HarrisCountyAI.Domain.ValueObjects;`. Replace the body after the `match is null` guard:

```csharp
        if (string.IsNullOrWhiteSpace(match.Field.Value))
        {
            // The printed label is what a reviewer scans for; a blank field's
            // value region is absent or too small to see.
            var labelBox = match.Field.KeyBoundingBox ?? match.Field.ValueBoundingBox;
            return Task.FromResult(Result(
                ValidationStatus.Missing,
                $"Field '{match.Field.Name}' is present but has no value.",
                sourceDocumentId: match.Document.Id,
                page: labelBox?.PageNumber ?? match.Field.PageNumber,
                boundingBox: labelBox));
        }

        var valueBox = match.Field.ValueBoundingBox ?? match.Field.KeyBoundingBox;
        return Task.FromResult(Result(
            ValidationStatus.Complete,
            $"Field '{match.Field.Name}' is present.",
            extractedValue: match.Field.Value,
            sourceDocumentId: match.Document.Id,
            page: valueBox?.PageNumber ?? match.Field.PageNumber,
            boundingBox: valueBox));
```

- [ ] **Step 5: Resolve the region in `DateRule`**

In `DateRule.cs`, add `using HarrisCountyAI.Domain.ValueObjects;`. Immediately after the `match is null` guard, compute both once:

```csharp
        var labelBox = match.Field.KeyBoundingBox ?? match.Field.ValueBoundingBox;
        var valueBox = match.Field.ValueBoundingBox ?? match.Field.KeyBoundingBox;
```

Then the empty branch uses `labelBox`:

```csharp
        var rawValue = match.Field.Value;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Task.FromResult(Result(
                ValidationStatus.Missing,
                $"Date field '{match.Field.Name}' is present but has no value.",
                sourceDocumentId: match.Document.Id,
                page: labelBox?.PageNumber ?? match.Field.PageNumber,
                boundingBox: labelBox));
        }
```

and each of the four remaining returns — unparseable, future, too old, and valid — adds the same two arguments, replacing its existing `page:` argument:

```csharp
                page: valueBox?.PageNumber ?? match.Field.PageNumber,
                boundingBox: valueBox));
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~RequiredFieldRuleTests|FullyQualifiedName~DateRuleTests"`
Expected: PASS — the nine new tests plus every pre-existing rule test.

- [ ] **Step 7: Commit**

```bash
git add backend/tests/HarrisCountyAI.UnitTests/Validation/NormalizedDocumentBuilder.cs \
        backend/src/HarrisCountyAI.Application/Validation/Rules/RequiredFieldRule.cs \
        backend/src/HarrisCountyAI.Application/Validation/Rules/DateRule.cs \
        backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/RequiredFieldRuleTests.cs \
        backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/DateRuleTests.cs
git commit -m "Report the evidence region from field and date rules"
```

---

### Task 6: Resolve regions in the signature and checkbox rules

**Files:**
- Modify: `backend/src/HarrisCountyAI.Application/Validation/Rules/SignatureRule.cs`
- Modify: `backend/src/HarrisCountyAI.Application/Validation/Rules/CheckboxRule.cs`
- Test: `backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/SignatureRuleTests.cs`, `.../CheckboxRuleTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3–5.
- Produces: no new public surface.

**Behavior change to call out in the PR description.** `CheckboxRule`'s present-but-unchecked branch (`if (foundAny)`) currently passes neither `sourceDocumentId` nor `page`, unlike every other rule's found-but-failing branch. Findings from that branch have no document link at all, so the report shows no **View page** button for them today and there is nowhere to hang a region. The rule must keep the field it matched while looping so that branch can report document, page, and region. This changes existing output — those findings gain a document link they did not have — and is required by the locked decision that checkbox findings carry a region.

- [ ] **Step 1: Write the failing tests**

Append to `backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/SignatureRuleTests.cs` (add `using HarrisCountyAI.Domain.ValueObjects;`):

```csharp
    private static BoundingBox MarkRegion(int pageNumber) => new()
    {
        PageNumber = pageNumber,
        X = 0.15,
        Y = 0.8,
        Width = 0.25,
        Height = 0.04,
    };

    [Fact]
    public async Task Reports_The_Mark_Region_When_The_Signature_Is_Present()
    {
        var region = MarkRegion(2);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithSignature("applicant signature", isSigned: true, page: 2, valueBox: region)
            .Build();
        var rule = new SignatureRule("Applicant signature", "applicant signature");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(region, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Mark_Region_When_The_Signature_Is_Present_But_Unsigned()
    {
        var region = MarkRegion(2);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithSignature("applicant signature", isSigned: false, page: 2, valueBox: region)
            .Build();
        var rule = new SignatureRule("Applicant signature", "applicant signature");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(region, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_No_Region_When_The_Signature_Field_Was_Never_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor")
            .Build();
        var rule = new SignatureRule("Applicant signature", "applicant signature");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Null(result.BoundingBox);
    }
```

Append to `backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/CheckboxRuleTests.cs` (add `using HarrisCountyAI.Domain.ValueObjects;` and a local `MarkRegion` helper identical to the one above):

```csharp
    private static BoundingBox MarkRegion(int pageNumber) => new()
    {
        PageNumber = pageNumber,
        X = 0.15,
        Y = 0.8,
        Width = 0.25,
        Height = 0.04,
    };

    [Fact]
    public async Task Reports_The_Mark_Region_When_A_Checkbox_Is_Checked()
    {
        var region = MarkRegion(1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("accessory building", isChecked: true, page: 1, valueBox: region)
            .Build();
        var rule = new CheckboxRule("Type of construction", "accessory building");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(region, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_Document_Page_And_Region_When_A_Checkbox_Is_Found_But_Unchecked()
    {
        var region = MarkRegion(1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("accessory building", isChecked: false, page: 1, valueBox: region)
            .Build();
        var rule = new CheckboxRule("Type of construction", "accessory building");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(document.Id, result.SourceDocumentId);
        Assert.Equal(1, result.Page);
        Assert.Equal(region, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_First_Found_Region_When_No_Checkbox_In_A_Group_Is_Checked()
    {
        var firstRegion = MarkRegion(1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("single family dwelling", isChecked: false, page: 1, valueBox: firstRegion)
            .WithCheckbox("swimming pool", isChecked: false, page: 1, valueBox: MarkRegion(2))
            .Build();
        var rule = new CheckboxRule(
            "Type of construction",
            [["single family dwelling"], ["swimming pool"]]);

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(firstRegion, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_No_Region_When_No_Checkbox_Was_Found_At_All()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor")
            .Build();
        var rule = new CheckboxRule("Type of construction", "accessory building");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Null(result.SourceDocumentId);
        Assert.Null(result.BoundingBox);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~SignatureRuleTests|FullyQualifiedName~CheckboxRuleTests"`
Expected: FAIL — regions are null, and the unchecked-checkbox test also fails on `SourceDocumentId`.

- [ ] **Step 3: Resolve the region in `SignatureRule`**

In `SignatureRule.cs`, add `using HarrisCountyAI.Domain.ValueObjects;`. After the `match is null` guard, compute the region once and pass it to both remaining returns. A signature recognized as a selection mark carries its region on `ValueBoundingBox`; one recognized as a key/value pair may carry only a key region, so fall back:

```csharp
        var markBox = match.Field.ValueBoundingBox ?? match.Field.KeyBoundingBox;

        if (match.Field.IsSigned == true)
        {
            return Task.FromResult(Result(
                ValidationStatus.Complete,
                $"Signature field '{match.Field.Name}' is signed.",
                extractedValue: match.Field.Value,
                sourceDocumentId: match.Document.Id,
                page: markBox?.PageNumber ?? match.Field.PageNumber,
                boundingBox: markBox));
        }

        return Task.FromResult(Result(
            ValidationStatus.Missing,
            $"Signature field '{match.Field.Name}' is present but not signed.",
            sourceDocumentId: match.Document.Id,
            page: markBox?.PageNumber ?? match.Field.PageNumber,
            boundingBox: markBox));
```

- [ ] **Step 4: Resolve the region in `CheckboxRule` and give its unchecked branch a source**

In `CheckboxRule.cs`, add `using HarrisCountyAI.Domain.ValueObjects;`. Replace the `foundAny` flag with the first match found, so the unchecked branch has a document, page, and region to report:

```csharp
        FieldMatch? firstFound = null;
        foreach (var nameVariants in _checkboxes)
        {
            var match = context.FindField(nameVariants, _documentType);
            if (match is null)
            {
                continue;
            }

            firstFound ??= match;

            if (match.Field.IsChecked == true)
            {
                var checkedBox = match.Field.ValueBoundingBox ?? match.Field.KeyBoundingBox;
                return Task.FromResult(Result(
                    ValidationStatus.Complete,
                    $"Checkbox '{match.Field.Name}' is checked.",
                    extractedValue: match.Field.Name,
                    sourceDocumentId: match.Document.Id,
                    page: checkedBox?.PageNumber ?? match.Field.PageNumber,
                    boundingBox: checkedBox));
            }
        }

        if (firstFound is { } found)
        {
            // Previously this branch reported no document at all, so the
            // finding could not be opened from the report. It points at the
            // first box the rule located.
            var foundBox = found.Field.ValueBoundingBox ?? found.Field.KeyBoundingBox;
            return Task.FromResult(Result(
                ValidationStatus.Missing,
                _checkboxes.Count == 1
                    ? $"Checkbox '{_checkboxes[0].First()}' is present but not checked."
                    : $"None of the checkboxes for '{Requirement}' are checked.",
                sourceDocumentId: found.Document.Id,
                page: foundBox?.PageNumber ?? found.Field.PageNumber,
                boundingBox: foundBox));
        }

        return Task.FromResult(Result(
            ValidationStatus.Missing,
            _checkboxes.Count == 1
                ? $"Checkbox '{_checkboxes[0].First()}' was not found in the submitted documents."
                : $"No checkboxes for '{Requirement}' were found in the submitted documents."));
```

`FieldMatch` is in `HarrisCountyAI.Domain.Validation`, already imported by the file.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~SignatureRuleTests|FullyQualifiedName~CheckboxRuleTests"`
Expected: PASS — the seven new tests plus every pre-existing rule test.

- [ ] **Step 6: Run the whole unit suite for regressions**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests`
Expected: PASS. Workflow-level tests that assert on checkbox findings may now see a `SourceDocumentId` where they previously saw null — if any fail, update the expectation, since the new value is correct.

- [ ] **Step 7: Commit**

```bash
git add backend/src/HarrisCountyAI.Application/Validation/Rules/SignatureRule.cs \
        backend/src/HarrisCountyAI.Application/Validation/Rules/CheckboxRule.cs \
        backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/SignatureRuleTests.cs \
        backend/tests/HarrisCountyAI.UnitTests/Validation/Rules/CheckboxRuleTests.cs
git commit -m "Report the evidence region from signature and checkbox rules"
```

---

### Task 7: Persist regions

**Files:**
- Modify: `backend/src/HarrisCountyAI.Infrastructure/Persistence/Configurations/NormalizedDocumentConfiguration.cs`
- Modify: `backend/src/HarrisCountyAI.Infrastructure/Persistence/Configurations/ValidationReportConfiguration.cs`
- Create: a migration under `backend/src/HarrisCountyAI.Infrastructure/Persistence/Migrations/`
- Test: `backend/tests/HarrisCountyAI.IntegrationTests/Persistence/NormalizedDocumentPersistenceTests.cs`, `.../ValidationReportPersistenceTests.cs`

**Interfaces:**
- Consumes: `DocumentField.KeyBoundingBox` / `.ValueBoundingBox` (Task 3), `ValidationReportItem.BoundingBox` (Task 4).
- Produces: no new code surface — schema only.

`BoundingBox` nests inside collections that are **already** owned (`OwnsMany` on both). Owned types flatten to columns on the existing tables, not new tables: `NormalizedDocumentFields` gains ten columns and `ValidationReportItems` gains five. All are nullable.

- [ ] **Step 1: Write the failing tests**

Append to `NormalizedDocumentPersistenceTests.cs` (add `using HarrisCountyAI.Domain.ValueObjects;`). The existing `CreateNormalizedDocument()` helper already produces the third field, `"applicant signature"`, with no regions, which covers the null case:

```csharp
    [Fact]
    public async Task Field_Bounding_Boxes_Round_Trip()
    {
        var keyBox = new BoundingBox { PageNumber = 1, X = 0.1, Y = 0.2, Width = 0.3, Height = 0.04 };
        var valueBox = new BoundingBox { PageNumber = 2, X = 0.5, Y = 0.6, Width = 0.2, Height = 0.03 };

        var document = CreateNormalizedDocument();
        var field = document.Fields.Single(f => f.Name == "applicant name");
        field.KeyBoundingBox = keyBox;
        field.ValueBoundingBox = valueBox;

        await using (var context = _database.CreateContext())
        {
            context.NormalizedDocuments.Add(document);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var loaded = await context.NormalizedDocuments
                .SingleAsync(d => d.Id == document.Id);

            var reloaded = loaded.Fields.Single(f => f.Name == "applicant name");
            Assert.Equal(keyBox, reloaded.KeyBoundingBox);
            Assert.Equal(valueBox, reloaded.ValueBoundingBox);

            // Fields extracted before regions existed keep null boxes rather
            // than materializing an empty one.
            var withoutRegions = loaded.Fields.Single(f => f.Name == "applicant signature");
            Assert.Null(withoutRegions.KeyBoundingBox);
            Assert.Null(withoutRegions.ValueBoundingBox);
        }
    }
```

Append to `ValidationReportPersistenceTests.cs` (add the same `using`). Its `CreateReport()` helper already produces a second item, the `RequiredDocumentRule(Site plan)` one, with no region:

```csharp
    [Fact]
    public async Task Report_Item_Bounding_Box_Round_Trips()
    {
        var box = new BoundingBox { PageNumber = 1, X = 0.1, Y = 0.2, Width = 0.3, Height = 0.04 };

        var report = CreateReport();
        var item = report.Items.Single(i => i.Requirement == "Owner name");
        item.BoundingBox = box;

        await using (var context = _database.CreateContext())
        {
            context.ValidationReports.Add(report);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var loaded = await context.ValidationReports
                .SingleAsync(r => r.Id == report.Id);

            Assert.Equal(box, loaded.Items.Single(i => i.Requirement == "Owner name").BoundingBox);
            Assert.Null(loaded.Items.Single(i => i.Requirement == "Site plan").BoundingBox);
        }
    }
```

Owned types are auto-included by EF Core, so no `Include` call is needed — matching how the existing round-trip tests read `Pages` and `Fields`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/HarrisCountyAI.IntegrationTests --filter "FullyQualifiedName~Persistence"`
Expected: FAIL — EF has no mapping for the new properties, so the model cannot be built.

Requires SQL Server running: `docker compose up -d`.

- [ ] **Step 3: Map the owned boxes on `NormalizedDocumentFields`**

In `NormalizedDocumentConfiguration.cs`, inside the existing `builder.OwnsMany(d => d.Fields, fields => { ... })` block, after `fields.Property(f => f.PageNumber);`:

```csharp
            // Owned types flatten to columns on NormalizedDocumentFields; the
            // navigation is required so EF materializes a null box rather than
            // throwing when every column is null.
            fields.OwnsOne(f => f.KeyBoundingBox, box =>
            {
                box.Property(b => b.PageNumber).HasColumnName("KeyBoundingBox_PageNumber");
                box.Property(b => b.X).HasColumnName("KeyBoundingBox_X");
                box.Property(b => b.Y).HasColumnName("KeyBoundingBox_Y");
                box.Property(b => b.Width).HasColumnName("KeyBoundingBox_Width");
                box.Property(b => b.Height).HasColumnName("KeyBoundingBox_Height");
            });

            fields.OwnsOne(f => f.ValueBoundingBox, box =>
            {
                box.Property(b => b.PageNumber).HasColumnName("ValueBoundingBox_PageNumber");
                box.Property(b => b.X).HasColumnName("ValueBoundingBox_X");
                box.Property(b => b.Y).HasColumnName("ValueBoundingBox_Y");
                box.Property(b => b.Width).HasColumnName("ValueBoundingBox_Width");
                box.Property(b => b.Height).HasColumnName("ValueBoundingBox_Height");
            });
```

- [ ] **Step 4: Map the owned box on `ValidationReportItems`**

In `ValidationReportConfiguration.cs`, inside the existing `builder.OwnsMany(r => r.Items, items => { ... })` block, after `items.Property(i => i.PageNumber);`:

```csharp
            items.OwnsOne(i => i.BoundingBox, box =>
            {
                box.Property(b => b.PageNumber).HasColumnName("BoundingBox_PageNumber");
                box.Property(b => b.X).HasColumnName("BoundingBox_X");
                box.Property(b => b.Y).HasColumnName("BoundingBox_Y");
                box.Property(b => b.Width).HasColumnName("BoundingBox_Width");
                box.Property(b => b.Height).HasColumnName("BoundingBox_Height");
            });
```

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add AddDocumentRegions \
  --project backend/src/HarrisCountyAI.Infrastructure \
  --startup-project backend/src/HarrisCountyAI.Api
```

Open the generated migration and confirm it contains **only** `AddColumn` calls — ten on `NormalizedDocumentFields`, five on `ValidationReportItems`, every one `nullable: true`. If it proposes dropping or recreating a table, stop: the owned-type configuration is wrong, not the migration.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HarrisCountyAI.IntegrationTests --filter "FullyQualifiedName~Persistence"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/HarrisCountyAI.Infrastructure/Persistence/Configurations/NormalizedDocumentConfiguration.cs \
        backend/src/HarrisCountyAI.Infrastructure/Persistence/Configurations/ValidationReportConfiguration.cs \
        backend/src/HarrisCountyAI.Infrastructure/Persistence/Migrations/ \
        backend/tests/HarrisCountyAI.IntegrationTests/Persistence/
git commit -m "Persist document evidence regions"
```

---

### Task 8: Expose the region on the API contract

**Files:**
- Modify: `backend/src/HarrisCountyAI.Application/Validation/ValidationReportDto.cs`
- Test: `backend/tests/HarrisCountyAI.UnitTests/Validation/Reports/` (add to the DTO test file if one exists there; otherwise create `ValidationReportDtoTests.cs`)

**Interfaces:**
- Consumes: `ValidationReportItem.BoundingBox` (Task 4).
- Produces: `ValidationReportItemDto.BoundingBox` (`BoundingBox?`) — the shape PR-B mirrors in `validation.model.ts`.

`ValidationReportItemDto` already exposes Domain enums directly, so exposing the Domain `BoundingBox` record follows the file's existing convention rather than adding a parallel wire type. **This is where PR-A stops** — no frontend file is touched.

- [ ] **Step 1: Write the failing test**

```csharp
using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

public class ValidationReportDtoTests
{
    private static ValidationReport ReportWith(BoundingBox? box) => new()
    {
        Id = Guid.NewGuid(),
        CaseId = Guid.NewGuid(),
        WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
        CreatedAt = DateTime.UtcNow,
        Items =
        [
            new ValidationReportItem
            {
                Id = Guid.NewGuid(),
                Order = 0,
                RuleName = "RequiredFieldRule(HCAD account number)",
                Requirement = "HCAD account number",
                ValidationType = ValidationType.Deterministic,
                Status = ValidationStatus.Missing,
                Message = "Field 'hcad account number' is present but has no value.",
                PageNumber = box?.PageNumber,
                BoundingBox = box,
            },
        ],
    };

    [Fact]
    public void Exposes_The_Region_On_The_Item()
    {
        var box = new BoundingBox { PageNumber = 1, X = 0.1, Y = 0.2, Width = 0.3, Height = 0.04 };

        var dto = ValidationReportDto.FromEntity(ReportWith(box));

        Assert.Equal(box, dto.Items.Single().BoundingBox);
    }

    [Fact]
    public void Leaves_The_Region_Null_When_The_Item_Has_None()
    {
        var dto = ValidationReportDto.FromEntity(ReportWith(box: null));

        Assert.Null(dto.Items.Single().BoundingBox);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~ValidationReportDtoTests"`
Expected: FAIL — `ValidationReportItemDto` has no `BoundingBox`.

- [ ] **Step 3: Add the property to the DTO**

In `ValidationReportDto.cs`, add `using HarrisCountyAI.Domain.ValueObjects;`, add a trailing positional parameter to the record, and pass it in `FromEntity`:

```csharp
public sealed record ValidationReportItemDto(
    Guid Id,
    string RuleName,
    string Requirement,
    ValidationType ValidationType,
    ValidationStatus Status,
    string Message,
    string? ExtractedValue,
    Guid? DocumentId,
    DocumentType? DocumentType,
    int? PageNumber,
    BoundingBox? BoundingBox)
{
    public static ValidationReportItemDto FromEntity(ValidationReportItem item) => new(
        item.Id,
        item.RuleName,
        item.Requirement,
        item.ValidationType,
        item.Status,
        item.Message,
        item.ExtractedValue,
        item.DocumentId,
        item.DocumentType,
        item.PageNumber,
        item.BoundingBox);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/HarrisCountyAI.UnitTests --filter "FullyQualifiedName~ValidationReportDtoTests"`
Expected: PASS.

- [ ] **Step 5: Run the full build and suite**

```bash
dotnet build
dotnet test
```

Expected: PASS. Any API contract or end-to-end test asserting the exact JSON shape of a validation report item will now see an extra `boundingBox` property — update those expectations.

- [ ] **Step 6: Update the API documentation**

Add `boundingBox` to the validation report item schema in `docs/api/endpoints.md`, describing it as a nullable region normalized to 0–1 page fractions with the origin at the top-left, and noting that null means the finding could not be located on the page.

- [ ] **Step 7: Commit**

```bash
git add backend/src/HarrisCountyAI.Application/Validation/ValidationReportDto.cs \
        backend/tests/HarrisCountyAI.UnitTests/Validation/Reports/ValidationReportDtoTests.cs \
        docs/api/endpoints.md
git commit -m "Expose the evidence region on the validation report contract"
```

---

## Definition of Done

- [ ] `dotnet build` succeeds with no new warnings.
- [ ] `dotnet test` passes — unit, integration, and architecture suites.
- [ ] The migration adds only nullable columns and drops nothing.
- [ ] No file under `frontend/` was modified.
- [ ] `docs/api/endpoints.md` documents the new field.
- [ ] The PR description records the `CheckboxRule` behavior change: findings from the present-but-unchecked branch now carry a document id and page they previously lacked.
- [ ] No commit message references a PR or task number, and none carries AI attribution.

## Notes for the PR description

**Reason for the change.** Validation findings name a page but not a place on it, so a reviewer hunts for the field by hand. Azure Document Intelligence already returns a bounding polygon for every key, value, and selection mark; the mapper was discarding them. This PR keeps them so PR-B can draw the region.

**Known limitations.**
- Documents normalized before this change carry null regions until re-extracted. There is no backfill job; the UI states when a finding cannot be located.
- Semantic and comparison findings, which do not resolve to a single extracted field, carry no region and will show the same "couldn't locate" copy in PR-B. Accepted for v1.
- Regions are axis-aligned rectangles derived from the recognition quadrilateral, so heavily rotated text yields a box slightly larger than the text itself.
