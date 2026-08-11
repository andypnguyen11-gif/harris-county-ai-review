using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Domain;

public class KnowledgeDocumentTests
{
    private static KnowledgeDocument CreateDocument() => KnowledgeDocument.Create(
        Guid.NewGuid(),
        "Floodplain Development Permit Application",
        "permit-application.pdf",
        "knowledge/permit-application.pdf",
        "Engineering",
        "PermitForm",
        "FloodplainDevelopment");

    [Fact]
    public void Create_Sets_Initial_State()
    {
        var id = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 1, 1);
        var before = DateTime.UtcNow;

        var document = KnowledgeDocument.Create(
            id,
            "Floodplain Regulations",
            "regulations.pdf",
            "knowledge/regulations.pdf",
            "Engineering",
            "Regulation",
            "FloodplainDevelopment",
            "2026.1",
            effectiveDate,
            "https://www.harriscountytx.gov/regulations.pdf");

        var after = DateTime.UtcNow;

        Assert.Equal(id, document.Id);
        Assert.Equal("Floodplain Regulations", document.Title);
        Assert.Equal("regulations.pdf", document.FileName);
        Assert.Equal("knowledge/regulations.pdf", document.BlobPath);
        Assert.Equal("Engineering", document.Department);
        Assert.Equal("Regulation", document.DocumentType);
        Assert.Equal("FloodplainDevelopment", document.PermitType);
        Assert.Equal("2026.1", document.Version);
        Assert.Equal(effectiveDate, document.EffectiveDate);
        Assert.Equal("https://www.harriscountytx.gov/regulations.pdf", document.SourceUrl);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Uploaded, document.IngestionStatus);
        Assert.Null(document.IngestionDate);
        Assert.InRange(document.CreatedAt, before, after);
        Assert.Equal(document.CreatedAt, document.UpdatedAt);
    }

    [Fact]
    public void Create_Defaults_Optional_Fields_To_Null()
    {
        var document = CreateDocument();

        Assert.Null(document.Version);
        Assert.Null(document.EffectiveDate);
        Assert.Null(document.SourceUrl);
    }

    [Fact]
    public void Create_Trims_Fields()
    {
        var document = KnowledgeDocument.Create(
            Guid.NewGuid(),
            "  Title  ",
            "  file.pdf  ",
            "  knowledge/file.pdf  ",
            "  Engineering  ",
            "  Checklist  ",
            "  FloodplainDevelopment  ",
            "  v2  ",
            null,
            "  https://example.gov/file.pdf  ");

        Assert.Equal("Title", document.Title);
        Assert.Equal("file.pdf", document.FileName);
        Assert.Equal("knowledge/file.pdf", document.BlobPath);
        Assert.Equal("Engineering", document.Department);
        Assert.Equal("Checklist", document.DocumentType);
        Assert.Equal("FloodplainDevelopment", document.PermitType);
        Assert.Equal("v2", document.Version);
        Assert.Equal("https://example.gov/file.pdf", document.SourceUrl);
    }

    [Fact]
    public void Create_Normalizes_Whitespace_Optional_Fields_To_Null()
    {
        var document = KnowledgeDocument.Create(
            Guid.NewGuid(),
            "Title",
            "file.pdf",
            "knowledge/file.pdf",
            "Engineering",
            "FAQ",
            "FloodplainDevelopment",
            "   ",
            null,
            "   ");

        Assert.Null(document.Version);
        Assert.Null(document.SourceUrl);
    }

    [Fact]
    public void Create_Rejects_Empty_Id()
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(
            Guid.Empty, "Title", "file.pdf", "knowledge/file.pdf", "Engineering", "Manual", "FloodplainDevelopment"));

        Assert.Equal("id", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_Title(string? title)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(
            Guid.NewGuid(), title!, "file.pdf", "knowledge/file.pdf", "Engineering", "Manual", "FloodplainDevelopment"));

        Assert.Equal("title", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_FileName(string? fileName)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(
            Guid.NewGuid(), "Title", fileName!, "knowledge/file.pdf", "Engineering", "Manual", "FloodplainDevelopment"));

        Assert.Equal("fileName", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_BlobPath(string? blobPath)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(
            Guid.NewGuid(), "Title", "file.pdf", blobPath!, "Engineering", "Manual", "FloodplainDevelopment"));

        Assert.Equal("blobPath", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_Department(string? department)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(
            Guid.NewGuid(), "Title", "file.pdf", "knowledge/file.pdf", department!, "Manual", "FloodplainDevelopment"));

        Assert.Equal("department", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_DocumentType(string? documentType)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(
            Guid.NewGuid(), "Title", "file.pdf", "knowledge/file.pdf", "Engineering", documentType!, "FloodplainDevelopment"));

        Assert.Equal("documentType", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_PermitType(string? permitType)
    {
        var exception = Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(
            Guid.NewGuid(), "Title", "file.pdf", "knowledge/file.pdf", "Engineering", "Manual", permitType!));

        Assert.Equal("permitType", exception.ParamName);
    }

    [Fact]
    public void MarkProcessing_From_Uploaded_Transitions_To_Processing()
    {
        var document = CreateDocument();

        document.MarkProcessing();

        Assert.Equal(KnowledgeDocumentIngestionStatus.Processing, document.IngestionStatus);
        Assert.True(document.UpdatedAt >= document.CreatedAt);
    }

    [Fact]
    public void MarkProcessing_While_Processing_Throws()
    {
        var document = CreateDocument();
        document.MarkProcessing();

        Assert.Throws<InvalidOperationException>(document.MarkProcessing);
    }

    [Fact]
    public void MarkProcessing_From_Failed_Allows_Retry()
    {
        var document = CreateDocument();
        document.MarkProcessing();
        document.MarkFailed();

        document.MarkProcessing();

        Assert.Equal(KnowledgeDocumentIngestionStatus.Processing, document.IngestionStatus);
    }

    [Fact]
    public void MarkProcessing_From_Ingested_Allows_Reingestion()
    {
        var document = CreateDocument();
        document.MarkProcessing();
        document.MarkIngested();

        document.MarkProcessing();

        Assert.Equal(KnowledgeDocumentIngestionStatus.Processing, document.IngestionStatus);
    }

    [Fact]
    public void MarkProcessing_When_Deactivated_Throws()
    {
        var document = CreateDocument();
        document.Deactivate();

        Assert.Throws<InvalidOperationException>(document.MarkProcessing);
    }

    [Fact]
    public void MarkIngested_From_Processing_Sets_IngestionDate()
    {
        var document = CreateDocument();
        document.MarkProcessing();
        var before = DateTime.UtcNow;

        document.MarkIngested();

        var after = DateTime.UtcNow;
        Assert.Equal(KnowledgeDocumentIngestionStatus.Ingested, document.IngestionStatus);
        Assert.NotNull(document.IngestionDate);
        Assert.InRange(document.IngestionDate.Value, before, after);
    }

    [Fact]
    public void MarkIngested_Without_Processing_Throws()
    {
        var document = CreateDocument();

        Assert.Throws<InvalidOperationException>(document.MarkIngested);
    }

    [Fact]
    public void MarkFailed_From_Processing_Transitions_To_Failed()
    {
        var document = CreateDocument();
        document.MarkProcessing();

        document.MarkFailed();

        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Null(document.IngestionDate);
    }

    [Fact]
    public void MarkFailed_Without_Processing_Throws()
    {
        var document = CreateDocument();

        Assert.Throws<InvalidOperationException>(document.MarkFailed);
    }

    [Fact]
    public void Deactivate_Transitions_To_Deactivated()
    {
        var document = CreateDocument();

        document.Deactivate();

        Assert.Equal(KnowledgeDocumentIngestionStatus.Deactivated, document.IngestionStatus);
    }

    [Fact]
    public void Deactivate_From_Ingested_Keeps_IngestionDate()
    {
        var document = CreateDocument();
        document.MarkProcessing();
        document.MarkIngested();

        document.Deactivate();

        Assert.Equal(KnowledgeDocumentIngestionStatus.Deactivated, document.IngestionStatus);
        Assert.NotNull(document.IngestionDate);
    }

    [Fact]
    public void Deactivate_Is_Idempotent()
    {
        var document = CreateDocument();
        document.Deactivate();
        var updatedAt = document.UpdatedAt;

        document.Deactivate();

        Assert.Equal(KnowledgeDocumentIngestionStatus.Deactivated, document.IngestionStatus);
        Assert.Equal(updatedAt, document.UpdatedAt);
    }
}
