using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Domain;

public class DocumentTests
{
    private static readonly Guid CaseId = Guid.NewGuid();

    [Fact]
    public void Create_Sets_Initial_State()
    {
        var before = DateTime.UtcNow;

        var document = Document.Create(CaseId, "elevation-cert.pdf", "cases/abc/elevation-cert.pdf", DocumentType.ElevationCertificate);

        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Equal(CaseId, document.CaseId);
        Assert.Equal("elevation-cert.pdf", document.FileName);
        Assert.Equal("cases/abc/elevation-cert.pdf", document.BlobPath);
        Assert.Equal(DocumentType.ElevationCertificate, document.DocumentType);
        Assert.Equal(DocumentProcessingStatus.Pending, document.ProcessingStatus);
        Assert.InRange(document.CreatedAt, before, after);
    }

    [Fact]
    public void Create_Rejects_Empty_CaseId()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Document.Create(Guid.Empty, "file.pdf", "path/file.pdf", DocumentType.Other));

        Assert.Equal("caseId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_FileName(string? fileName)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Document.Create(CaseId, fileName!, "path/file.pdf", DocumentType.Other));

        Assert.Equal("fileName", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_BlobPath(string? blobPath)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Document.Create(CaseId, "file.pdf", blobPath!, DocumentType.Other));

        Assert.Equal("blobPath", exception.ParamName);
    }

    [Fact]
    public void SetProcessingStatus_Updates_Status()
    {
        var document = Document.Create(CaseId, "file.pdf", "path/file.pdf", DocumentType.Other);

        document.SetProcessingStatus(DocumentProcessingStatus.Uploaded);

        Assert.Equal(DocumentProcessingStatus.Uploaded, document.ProcessingStatus);
    }

    [Fact]
    public void SetProcessingStatus_Rejects_Undefined_Value()
    {
        var document = Document.Create(CaseId, "file.pdf", "path/file.pdf", DocumentType.Other);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.SetProcessingStatus((DocumentProcessingStatus)999));
        Assert.Equal(DocumentProcessingStatus.Pending, document.ProcessingStatus);
    }
}
