using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Validation.Rules;

public class RequiredFieldRuleTests
{
    [Fact]
    public async Task Complete_When_Field_Present_With_Value()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("OWNER NAME", "Jane Smith", page: 2)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new RequiredFieldRule("Owner name", "Owner Name", documentType: DocumentType.PermitApplication);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal("Owner name", result.Requirement);
        Assert.Equal("Jane Smith", result.ExtractedValue);
        Assert.Equal(document.Id, result.SourceDocumentId);
        Assert.Equal(2, result.Page);
        Assert.Equal(ValidationType.Deterministic, result.ValidationType);
        Assert.Equal("RequiredFieldRule(Owner name)", result.RuleName);
    }

    [Fact]
    public async Task Complete_When_Field_Matches_A_Name_Variant()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("HCAD #", "1234567890123")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new RequiredFieldRule("HCAD account number", "HCAD Account Number", ["HCAD #", "HCAD Acct No"]);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal("1234567890123", result.ExtractedValue);
    }

    [Fact]
    public async Task Missing_When_Field_Not_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("ADDRESS", "123 Main St")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new RequiredFieldRule("Owner name", "Owner Name");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Null(result.ExtractedValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_When_Field_Present_But_Empty(string? value)
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("Owner Name", value, page: 1)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new RequiredFieldRule("Owner name", "Owner Name");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(document.Id, result.SourceDocumentId);
        Assert.Contains("no value", result.Message);
    }

    [Fact]
    public async Task UnableToDetermine_When_No_Documents_Extracted()
    {
        var context = NormalizedDocumentBuilder.ContextFor();
        var rule = new RequiredFieldRule("Owner name", "Owner Name");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
        Assert.Contains("No extracted documents", result.Message);
    }

    [Fact]
    public async Task UnableToDetermine_When_Scoped_Document_Type_Absent()
    {
        var sitePlan = new NormalizedDocumentBuilder(DocumentType.SitePlan)
            .WithTextField("Owner Name", "Jane Smith")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(sitePlan);
        var rule = new RequiredFieldRule("Owner name", "Owner Name", documentType: DocumentType.PermitApplication);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
        Assert.Contains("PermitApplication", result.Message);
    }
}
