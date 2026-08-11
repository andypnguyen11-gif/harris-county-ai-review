using Azure.AI.DocumentIntelligence;
using HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

namespace HarrisCountyAI.UnitTests.DocumentIntelligence;

public class AnalyzeResultMapperTests
{
    private readonly AnalyzeResultMapper _mapper = new();
    private static readonly Guid DocumentId = Guid.NewGuid();

    [Fact]
    public void Maps_Document_Level_Properties()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            modelId: "prebuilt-layout",
            content: "Permit Application");

        var before = DateTime.UtcNow;
        var extracted = _mapper.Map(DocumentId, result);

        Assert.Equal(DocumentId, extracted.DocumentId);
        Assert.Equal("prebuilt-layout", extracted.ModelId);
        Assert.Equal("Permit Application", extracted.RawText);
        Assert.InRange(extracted.ExtractedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public void Maps_Pages_With_Text_From_Content_Spans()
    {
        const string content = "Page one text.Page two text.";
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            content: content,
            pages:
            [
                DocumentIntelligenceModelFactory.DocumentPage(
                    pageNumber: 1,
                    spans: [DocumentIntelligenceModelFactory.DocumentSpan(0, 14)]),
                DocumentIntelligenceModelFactory.DocumentPage(
                    pageNumber: 2,
                    spans: [DocumentIntelligenceModelFactory.DocumentSpan(14, 14)]),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        Assert.Equal(2, extracted.Pages.Count);
        Assert.Equal(1, extracted.Pages[0].PageNumber);
        Assert.Equal("Page one text.", extracted.Pages[0].Text);
        Assert.Equal(2, extracted.Pages[1].PageNumber);
        Assert.Equal("Page two text.", extracted.Pages[1].Text);
    }

    [Fact]
    public void Falls_Back_To_Lines_When_Page_Spans_Are_Missing()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            content: "irrelevant",
            pages:
            [
                DocumentIntelligenceModelFactory.DocumentPage(
                    pageNumber: 1,
                    lines:
                    [
                        DocumentIntelligenceModelFactory.DocumentLine(content: "First line"),
                        DocumentIntelligenceModelFactory.DocumentLine(content: "Second line"),
                    ]),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        Assert.Equal("First line\nSecond line", extracted.Pages[0].Text);
    }

    [Fact]
    public void Assigns_Paragraphs_To_Their_Pages()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages:
            [
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1),
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 2),
            ],
            paragraphs:
            [
                DocumentIntelligenceModelFactory.DocumentParagraph(
                    content: "Paragraph on page one",
                    boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: 1)]),
                DocumentIntelligenceModelFactory.DocumentParagraph(
                    content: "Paragraph on page two",
                    boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: 2)]),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        Assert.Equal(["Paragraph on page one"], extracted.Pages[0].Paragraphs);
        Assert.Equal(["Paragraph on page two"], extracted.Pages[1].Paragraphs);
    }

    [Fact]
    public void Maps_Key_Value_Pairs_With_Confidence_And_Page_Number()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            keyValuePairs:
            [
                DocumentIntelligenceModelFactory.DocumentKeyValuePair(
                    key: DocumentIntelligenceModelFactory.DocumentKeyValueElement(
                        content: "Applicant Name:",
                        boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: 3)]),
                    value: DocumentIntelligenceModelFactory.DocumentKeyValueElement(content: "Jane Doe"),
                    confidence: 0.92f),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        var field = Assert.Single(extracted.KeyValuePairs);
        Assert.Equal("Applicant Name:", field.Key);
        Assert.Equal("Jane Doe", field.Value);
        Assert.NotNull(field.Confidence);
        Assert.Equal(0.92, field.Confidence.Value, precision: 4);
        Assert.Equal(3, field.PageNumber);
    }

    [Fact]
    public void Maps_Key_Value_Pair_Without_Value_Or_Regions()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            keyValuePairs:
            [
                DocumentIntelligenceModelFactory.DocumentKeyValuePair(
                    key: DocumentIntelligenceModelFactory.DocumentKeyValueElement(content: "Phone:"),
                    confidence: 0.5f),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        var field = Assert.Single(extracted.KeyValuePairs);
        Assert.Equal("Phone:", field.Key);
        Assert.Null(field.Value);
        Assert.Null(field.PageNumber);
    }

    [Fact]
    public void Skips_Key_Value_Pairs_Without_Key_Content()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            keyValuePairs:
            [
                DocumentIntelligenceModelFactory.DocumentKeyValuePair(
                    key: DocumentIntelligenceModelFactory.DocumentKeyValueElement(content: "  ")),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        Assert.Empty(extracted.KeyValuePairs);
    }

    [Fact]
    public void Maps_Selection_Marks_With_State_Confidence_And_Page()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages:
            [
                DocumentIntelligenceModelFactory.DocumentPage(
                    pageNumber: 2,
                    selectionMarks:
                    [
                        DocumentIntelligenceModelFactory.DocumentSelectionMark(
                            state: DocumentSelectionMarkState.Selected,
                            confidence: 0.87f),
                        DocumentIntelligenceModelFactory.DocumentSelectionMark(
                            state: DocumentSelectionMarkState.Unselected,
                            confidence: 0.65f),
                    ]),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        Assert.Equal(2, extracted.SelectionMarks.Count);
        Assert.True(extracted.SelectionMarks[0].IsSelected);
        Assert.False(extracted.SelectionMarks[1].IsSelected);
        Assert.All(extracted.SelectionMarks, mark => Assert.Equal(2, mark.PageNumber));
        var confidence = extracted.SelectionMarks[0].Confidence;
        Assert.NotNull(confidence);
        Assert.Equal(0.87, confidence.Value, precision: 4);
    }

    [Fact]
    public void Resolves_Selection_Mark_Name_From_Overlapping_Key_Value_Pair()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages:
            [
                DocumentIntelligenceModelFactory.DocumentPage(
                    pageNumber: 1,
                    selectionMarks:
                    [
                        DocumentIntelligenceModelFactory.DocumentSelectionMark(
                            state: DocumentSelectionMarkState.Selected,
                            span: DocumentIntelligenceModelFactory.DocumentSpan(40, 10)),
                        DocumentIntelligenceModelFactory.DocumentSelectionMark(
                            state: DocumentSelectionMarkState.Unselected,
                            span: DocumentIntelligenceModelFactory.DocumentSpan(90, 12)),
                    ]),
            ],
            keyValuePairs:
            [
                DocumentIntelligenceModelFactory.DocumentKeyValuePair(
                    key: DocumentIntelligenceModelFactory.DocumentKeyValueElement(content: "In Floodplain?"),
                    value: DocumentIntelligenceModelFactory.DocumentKeyValueElement(
                        content: ":selected:",
                        spans: [DocumentIntelligenceModelFactory.DocumentSpan(40, 10)])),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        Assert.Equal("In Floodplain?", extracted.SelectionMarks[0].Name);
        Assert.Null(extracted.SelectionMarks[1].Name);
    }

    [Fact]
    public void Maps_Tables_With_Cells_And_Page_Number()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            tables:
            [
                DocumentIntelligenceModelFactory.DocumentTable(
                    rowCount: 2,
                    columnCount: 2,
                    cells:
                    [
                        DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 0, columnIndex: 0, content: "Item"),
                        DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 0, columnIndex: 1, content: "Cost"),
                        DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 1, columnIndex: 0, content: "Culvert"),
                        DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 1, columnIndex: 1, content: "$1,200"),
                    ],
                    boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: 4)]),
            ]);

        var extracted = _mapper.Map(DocumentId, result);

        var table = Assert.Single(extracted.Tables);
        Assert.Equal(4, table.PageNumber);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal(4, table.Cells.Count);
        Assert.Equal("Culvert", table.Cells.Single(c => c.RowIndex == 1 && c.ColumnIndex == 0).Content);
    }

    [Fact]
    public void Maps_Empty_Result_To_Empty_Document()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult();

        var extracted = _mapper.Map(DocumentId, result);

        Assert.Empty(extracted.Pages);
        Assert.Empty(extracted.KeyValuePairs);
        Assert.Empty(extracted.SelectionMarks);
        Assert.Empty(extracted.Tables);
        Assert.Equal(string.Empty, extracted.RawText);
        Assert.Equal(string.Empty, extracted.ModelId);
    }
}
