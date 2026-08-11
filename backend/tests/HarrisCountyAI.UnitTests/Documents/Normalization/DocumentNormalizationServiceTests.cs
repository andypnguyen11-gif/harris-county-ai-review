using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Documents.Normalization;

public class DocumentNormalizationServiceTests
{
    private readonly DocumentNormalizationService _service = new();
    private static readonly Guid CaseId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();

    private static ExtractedDocument Extracted(
        IReadOnlyList<ExtractedPage>? pages = null,
        IReadOnlyList<ExtractedField>? keyValuePairs = null,
        IReadOnlyList<ExtractedSelectionMark>? selectionMarks = null,
        string rawText = "") => new()
        {
            DocumentId = DocumentId,
            Pages = pages ?? [],
            KeyValuePairs = keyValuePairs ?? [],
            SelectionMarks = selectionMarks ?? [],
            RawText = rawText,
            ModelId = "prebuilt-layout",
            ExtractedAt = DateTime.UtcNow,
        };

    [Fact]
    public void Sets_Document_Identity_And_Metadata()
    {
        var before = DateTime.UtcNow;
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted());

        Assert.NotEqual(Guid.Empty, normalized.Id);
        Assert.Equal(DocumentId, normalized.DocumentId);
        Assert.Equal(CaseId, normalized.CaseId);
        Assert.Equal(DocumentType.PermitApplication, normalized.DocumentType);
        Assert.InRange(normalized.CreatedAt, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData("Applicant Name:", "applicant name")]
    [InlineData("  Applicant   Name : ", "applicant name")]
    [InlineData("PERMIT\tNUMBER::", "permit number")]
    [InlineData("Date of Birth", "date of birth")]
    public void Normalizes_Field_Names(string rawKey, string expectedName)
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            keyValuePairs: [new ExtractedField { Key = rawKey, Value = "anything" }]));

        var field = Assert.Single(normalized.Fields);
        Assert.Equal(expectedName, field.Name);
    }

    [Fact]
    public void Skips_Key_Value_Pairs_Whose_Name_Normalizes_To_Empty()
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            keyValuePairs: [new ExtractedField { Key = " : ", Value = "orphan" }]));

        Assert.Empty(normalized.Fields);
    }

    [Theory]
    [InlineData("01/15/2026")]
    [InlineData("January 15, 2026")]
    [InlineData("2026-01-15")]
    public void Classifies_Date_Parseable_Values_As_Date(string value)
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            keyValuePairs: [new ExtractedField { Key = "Issue Date:", Value = value }]));

        Assert.Equal(FieldKind.Date, Assert.Single(normalized.Fields).Kind);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("3.14")]
    [InlineData("-17")]
    [InlineData("1,200.50")]
    public void Classifies_Numeric_Values_As_Number(string value)
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            keyValuePairs: [new ExtractedField { Key = "Lot Size:", Value = value }]));

        Assert.Equal(FieldKind.Number, Assert.Single(normalized.Fields).Kind);
    }

    [Theory]
    [InlineData("Jane Doe")]
    [InlineData("")]
    [InlineData(null)]
    public void Classifies_Other_Values_As_Text(string? value)
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            keyValuePairs: [new ExtractedField { Key = "Applicant Name:", Value = value }]));

        var field = Assert.Single(normalized.Fields);
        Assert.Equal(FieldKind.Text, field.Kind);
        Assert.Null(field.IsChecked);
        Assert.Null(field.IsSigned);
    }

    [Theory]
    [InlineData(":selected:", true)]
    [InlineData(":unselected:", false)]
    public void Classifies_Selection_Sentinel_Values_As_Checkbox(string value, bool expectedChecked)
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            keyValuePairs: [new ExtractedField { Key = "In Floodplain?", Value = value }]));

        var field = Assert.Single(normalized.Fields);
        Assert.Equal(FieldKind.Checkbox, field.Kind);
        Assert.Equal(expectedChecked, field.IsChecked);
    }

    [Fact]
    public void Selection_Marks_Become_Checkbox_Fields_With_IsChecked()
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            selectionMarks:
            [
                new ExtractedSelectionMark { Name = "Variance Requested", IsSelected = true, Confidence = 0.9, PageNumber = 2 },
                new ExtractedSelectionMark { Name = null, IsSelected = false, Confidence = 0.7, PageNumber = 3 },
            ]));

        Assert.Equal(2, normalized.Fields.Count);

        var named = normalized.Fields[0];
        Assert.Equal("variance requested", named.Name);
        Assert.Equal(FieldKind.Checkbox, named.Kind);
        Assert.True(named.IsChecked);
        Assert.Equal(0.9, named.Confidence);
        Assert.Equal(2, named.PageNumber);

        var unnamed = normalized.Fields[1];
        Assert.Equal("selection mark 2", unnamed.Name);
        Assert.False(unnamed.IsChecked);
        Assert.Equal(3, unnamed.PageNumber);
    }

    [Theory]
    [InlineData("Applicant Signature:", "Jane Doe", true)]
    [InlineData("Applicant Signature:", null, false)]
    [InlineData("Applicant Signature:", "   ", false)]
    [InlineData("Applicant Signature:", ":unselected:", false)]
    [InlineData("Applicant Signature:", ":selected:", true)]
    [InlineData("Signed by:", "J. Doe", true)]
    public void Keys_Containing_Signature_Or_Signed_Become_Signature_Fields(string key, string? value, bool expectedSigned)
    {
        var normalized = _service.Normalize(CaseId, DocumentType.Affidavit, Extracted(
            keyValuePairs: [new ExtractedField { Key = key, Value = value }]));

        var field = Assert.Single(normalized.Fields);
        Assert.Equal(FieldKind.Signature, field.Kind);
        Assert.Equal(expectedSigned, field.IsSigned);
        Assert.Null(field.IsChecked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selection_Marks_Named_Signature_Become_Signature_Fields(bool isSelected)
    {
        var normalized = _service.Normalize(CaseId, DocumentType.Affidavit, Extracted(
            selectionMarks: [new ExtractedSelectionMark { Name = "Notary Signature", IsSelected = isSelected, PageNumber = 1 }]));

        var field = Assert.Single(normalized.Fields);
        Assert.Equal(FieldKind.Signature, field.Kind);
        Assert.Equal(isSelected, field.IsSigned);
    }

    [Fact]
    public void Preserves_Page_Numbers_And_Confidence_From_Extraction()
    {
        var normalized = _service.Normalize(CaseId, DocumentType.PermitApplication, Extracted(
            keyValuePairs: [new ExtractedField { Key = "Permit Number:", Value = "FP-1234-X", Confidence = 0.85, PageNumber = 4 }]));

        var field = Assert.Single(normalized.Fields);
        Assert.Equal(0.85, field.Confidence);
        Assert.Equal(4, field.PageNumber);
    }

    [Fact]
    public void Preserves_Pages_With_Numbers_And_Text()
    {
        var normalized = _service.Normalize(CaseId, DocumentType.SitePlan, Extracted(
            pages:
            [
                new ExtractedPage { PageNumber = 1, Text = "First page" },
                new ExtractedPage { PageNumber = 2, Text = "Second page" },
            ]));

        Assert.Equal(2, normalized.Pages.Count);
        Assert.All(normalized.Pages, page => Assert.NotEqual(Guid.Empty, page.Id));
        Assert.Equal(1, normalized.Pages[0].PageNumber);
        Assert.Equal("First page", normalized.Pages[0].Text);
        Assert.Equal(2, normalized.Pages[1].PageNumber);
        Assert.Equal("Second page", normalized.Pages[1].Text);
    }

    [Fact]
    public void Joins_Page_Texts_Into_RawText()
    {
        var normalized = _service.Normalize(CaseId, DocumentType.SitePlan, Extracted(
            pages:
            [
                new ExtractedPage { PageNumber = 1, Text = "First page" },
                new ExtractedPage { PageNumber = 2, Text = "Second page" },
            ],
            rawText: "ignored when pages exist"));

        Assert.Equal("First page\nSecond page", normalized.RawText);
    }

    [Fact]
    public void Falls_Back_To_Extracted_RawText_When_There_Are_No_Pages()
    {
        var normalized = _service.Normalize(CaseId, DocumentType.SitePlan, Extracted(rawText: "flat text"));

        Assert.Equal("flat text", normalized.RawText);
    }

    [Fact]
    public void Empty_Extraction_Produces_Empty_Document()
    {
        var normalized = _service.Normalize(CaseId, DocumentType.Other, Extracted());

        Assert.Empty(normalized.Pages);
        Assert.Empty(normalized.Fields);
        Assert.Equal(string.Empty, normalized.RawText);
    }

    [Fact]
    public void Null_Extraction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Normalize(CaseId, DocumentType.Other, null!));
    }
}
