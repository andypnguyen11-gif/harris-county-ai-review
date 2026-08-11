using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.UnitTests.Validation;

public class ValidationContextTests
{
    [Theory]
    [InlineData("OWNER NAME", "ownername")]
    [InlineData("Owner Name:", "ownername")]
    [InlineData("owner_name", "ownername")]
    [InlineData("HCAD #", "hcad")]
    [InlineData("  Signature (APPLICANT)  ", "signatureapplicant")]
    public void NormalizeFieldName_Strips_Case_Whitespace_And_Punctuation(string input, string expected)
    {
        Assert.Equal(expected, ValidationContext.NormalizeFieldName(input));
    }

    [Fact]
    public void FindField_Matches_Name_Variants_Ignoring_Case_And_Punctuation()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("OWNER NAME:", "Jane Smith")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var match = context.FindField(["Name of Owner", "Owner Name"]);

        Assert.NotNull(match);
        Assert.Equal("OWNER NAME:", match.Field.Name);
        Assert.Equal(document.Id, match.Document.Id);
    }

    [Fact]
    public void FindField_Returns_Null_When_No_Variant_Matches()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("ADDRESS", "123 Main St")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        Assert.Null(context.FindField(["Owner Name"]));
    }

    [Fact]
    public void FindField_Honors_DocumentType_Scope()
    {
        var sitePlan = new NormalizedDocumentBuilder(DocumentType.SitePlan)
            .WithTextField("Date", "01/01/2026")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(sitePlan);

        Assert.Null(context.FindField(["Date"], DocumentType.PermitApplication));
        Assert.NotNull(context.FindField(["Date"], DocumentType.SitePlan));
    }

    [Fact]
    public void HasDocumentType_And_GetDocuments_Filter_By_Type()
    {
        var application = new NormalizedDocumentBuilder(DocumentType.PermitApplication).Build();
        var sitePlan = new NormalizedDocumentBuilder(DocumentType.SitePlan).Build();
        var context = NormalizedDocumentBuilder.ContextFor(application, sitePlan);

        Assert.True(context.HasDocumentType(DocumentType.SitePlan));
        Assert.False(context.HasDocumentType(DocumentType.ElevationCertificate));
        Assert.Equal([application.Id], context.GetDocuments(DocumentType.PermitApplication).Select(d => d.Id));
    }
}
