using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;

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
}
