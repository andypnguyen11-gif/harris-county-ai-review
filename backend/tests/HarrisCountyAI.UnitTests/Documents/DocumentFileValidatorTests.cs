using HarrisCountyAI.Application.Documents;

namespace HarrisCountyAI.UnitTests.Documents;

public class DocumentFileValidatorTests
{
    private readonly DocumentFileValidator _validator = new();

    [Theory]
    [InlineData("permit-application.pdf", "application/pdf")]
    [InlineData("scan.png", "image/png")]
    [InlineData("scan.jpg", "image/jpeg")]
    [InlineData("scan.jpeg", "image/jpeg")]
    [InlineData("scan.tif", "image/tiff")]
    [InlineData("scan.tiff", "image/tiff")]
    public void Validate_Accepts_Allowed_Extension_And_ContentType(string fileName, string contentType)
    {
        var result = _validator.Validate(fileName, contentType, 1024);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("REPORT.PDF", "application/pdf")]
    [InlineData("Scan.Png", "IMAGE/PNG")]
    public void Validate_Is_Case_Insensitive(string fileName, string contentType)
    {
        var result = _validator.Validate(fileName, contentType, 1024);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Accepts_ContentType_With_Parameters()
    {
        var result = _validator.Validate("report.pdf", "application/pdf; charset=utf-8", 1024);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("report.docx")]
    [InlineData("notes.txt")]
    public void Validate_Rejects_Disallowed_Extension(string fileName)
    {
        var result = _validator.Validate(fileName, "application/pdf", 1024);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("extension", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Rejects_FileName_Without_Extension()
    {
        var result = _validator.Validate("report", "application/pdf", 1024);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no extension", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Rejects_Missing_FileName(string? fileName)
    {
        var result = _validator.Validate(fileName, "application/pdf", 1024);

        Assert.False(result.IsValid);
        Assert.Contains("A file name is required.", result.Errors);
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    [InlineData("application/zip")]
    public void Validate_Rejects_Disallowed_ContentType(string contentType)
    {
        var result = _validator.Validate("report.pdf", contentType, 1024);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Content type", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Rejects_Missing_ContentType(string? contentType)
    {
        var result = _validator.Validate("report.pdf", contentType, 1024);

        Assert.False(result.IsValid);
        Assert.Contains("A content type is required.", result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Rejects_Empty_File(long fileSizeBytes)
    {
        var result = _validator.Validate("report.pdf", "application/pdf", fileSizeBytes);

        Assert.False(result.IsValid);
        Assert.Contains("The file is empty.", result.Errors);
    }

    [Fact]
    public void Validate_Accepts_Size_Of_One_Byte()
    {
        var result = _validator.Validate("report.pdf", "application/pdf", 1);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Accepts_Size_Exactly_At_Maximum()
    {
        var result = _validator.Validate(
            "report.pdf", "application/pdf", DocumentFileValidator.DefaultMaxFileSizeBytes);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Rejects_Size_One_Byte_Over_Maximum()
    {
        var result = _validator.Validate(
            "report.pdf", "application/pdf", DocumentFileValidator.DefaultMaxFileSizeBytes + 1);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("exceeds the maximum", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Uses_Configured_Maximum_Size()
    {
        var validator = new DocumentFileValidator(maxFileSizeBytes: 100);

        Assert.True(validator.Validate("report.pdf", "application/pdf", 100).IsValid);
        Assert.False(validator.Validate("report.pdf", "application/pdf", 101).IsValid);
    }

    [Fact]
    public void Validate_Default_Maximum_Is_50_MB()
    {
        Assert.Equal(52_428_800, DocumentFileValidator.DefaultMaxFileSizeBytes);
        Assert.Equal(52_428_800, _validator.MaxFileSizeBytes);
    }

    [Fact]
    public void Validate_Collects_All_Failures_At_Once()
    {
        var result = _validator.Validate(
            "malware.exe", "application/octet-stream", DocumentFileValidator.DefaultMaxFileSizeBytes + 1);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void Constructor_Rejects_NonPositive_Maximum_Size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentFileValidator(maxFileSizeBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentFileValidator(maxFileSizeBytes: -1));
    }
}
