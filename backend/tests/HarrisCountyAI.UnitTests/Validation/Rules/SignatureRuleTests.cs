using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Validation.Rules;

public class SignatureRuleTests
{
    private static SignatureRule Rule() =>
        new("Applicant signature", "Signature", ["Applicant Signature", "Signature (Applicant)"], DocumentType.PermitApplication);

    [Fact]
    public async Task Complete_When_Signature_Present_And_Signed()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithSignature("Signature (APPLICANT)", isSigned: true, page: 1)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(document.Id, result.SourceDocumentId);
        Assert.Equal(1, result.Page);
        Assert.Equal("SignatureRule(Applicant signature)", result.RuleName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Missing_When_Signature_Present_But_Unsigned(bool? isSigned)
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithSignature("Signature", isSigned)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Contains("not signed", result.Message);
        Assert.Equal(document.Id, result.SourceDocumentId);
    }

    [Fact]
    public async Task Missing_When_Signature_Field_Not_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("Print Name", "Jane Smith")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public async Task UnableToDetermine_When_Scoped_Document_Type_Absent()
    {
        var context = NormalizedDocumentBuilder.ContextFor(
            new NormalizedDocumentBuilder(DocumentType.SitePlan).Build());

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
    }

    [Fact]
    public async Task UnableToDetermine_When_No_Documents_Extracted()
    {
        var context = NormalizedDocumentBuilder.ContextFor();

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
    }

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
}
