using HarrisCountyAI.Application.Documents;

namespace HarrisCountyAI.UnitTests.Documents;

public class DocumentBlobPathBuilderTests
{
    [Fact]
    public void ForCaseDocument_Builds_CaseId_DocumentId_FileName_Path()
    {
        var caseId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var documentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var path = DocumentBlobPathBuilder.ForCaseDocument(caseId, documentId, "permit.pdf");

        Assert.Equal(
            "11111111-2222-3333-4444-555555555555/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_permit.pdf",
            path);
    }

    [Fact]
    public void ForCaseDocument_Sanitizes_The_FileName()
    {
        var caseId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var path = DocumentBlobPathBuilder.ForCaseDocument(caseId, documentId, "site plan (rev 2).pdf");

        Assert.Equal($"{caseId:D}/{documentId:D}_site-plan-rev-2.pdf", path);
    }

    [Theory]
    [InlineData("permit.pdf", "permit.pdf")]
    [InlineData("Permit Application.pdf", "Permit-Application.pdf")]
    [InlineData("scan#1@final!.png", "scan-1-final.png")]
    [InlineData("UPPER_lower-123.tiff", "UPPER_lower-123.tiff")]
    public void SanitizeFileName_Replaces_Unsafe_Characters(string input, string expected)
    {
        Assert.Equal(expected, DocumentBlobPathBuilder.SanitizeFileName(input));
    }

    [Theory]
    [InlineData("folder/permit.pdf", "permit.pdf")]
    [InlineData("..\\..\\windows\\evil.pdf", "evil.pdf")]
    [InlineData("/etc/passwd.pdf", "passwd.pdf")]
    public void SanitizeFileName_Strips_Directory_Components(string input, string expected)
    {
        Assert.Equal(expected, DocumentBlobPathBuilder.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_Collapses_Dot_Runs_So_Traversal_Cannot_Survive()
    {
        var sanitized = DocumentBlobPathBuilder.SanitizeFileName("report..pdf");

        Assert.Equal("report.pdf", sanitized);
        Assert.DoesNotContain("..", sanitized);
    }

    [Theory]
    [InlineData("  permit.pdf  ", "permit.pdf")]
    [InlineData("...permit.pdf", "permit.pdf")]
    [InlineData("--permit.pdf--", "permit.pdf")]
    public void SanitizeFileName_Trims_Leading_And_Trailing_Separators(string input, string expected)
    {
        Assert.Equal(expected, DocumentBlobPathBuilder.SanitizeFileName(input));
    }

    [Theory]
    [InlineData("###")]
    [InlineData("...")]
    [InlineData("日本語")]
    public void SanitizeFileName_Falls_Back_When_Nothing_Safe_Remains(string input)
    {
        Assert.Equal("file", DocumentBlobPathBuilder.SanitizeFileName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeFileName_Throws_For_Missing_Input(string input)
    {
        Assert.Throws<ArgumentException>(() => DocumentBlobPathBuilder.SanitizeFileName(input));
    }
}
