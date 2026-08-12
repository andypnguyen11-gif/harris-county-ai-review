using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.ValueObjects;

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

    private static BoundingBox RegionOn(int pageNumber, double x) => new()
    {
        PageNumber = pageNumber,
        X = x,
        Y = 0.3,
        Width = 0.2,
        Height = 0.03,
    };

    [Fact]
    public async Task Reports_The_Value_Region_When_The_Field_Has_A_Value()
    {
        var keyBox = RegionOn(1, 0.1);
        var valueBox = RegionOn(1, 0.5);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor", page: 1, keyBox: keyBox, valueBox: valueBox)
            .Build();
        var rule = new RequiredFieldRule("Owner name", "owner name");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(valueBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Key_Region_When_The_Field_Is_Present_But_Empty()
    {
        var keyBox = RegionOn(1, 0.1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("hcad account number", null, page: 1, keyBox: keyBox)
            .Build();
        var rule = new RequiredFieldRule("HCAD account number", "hcad account number");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(keyBox, result.BoundingBox);
    }

    [Fact]
    public async Task Falls_Back_To_The_Key_Region_When_There_Is_No_Value_Region()
    {
        var keyBox = RegionOn(1, 0.1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor", page: 1, keyBox: keyBox)
            .Build();
        var rule = new RequiredFieldRule("Owner name", "owner name");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(keyBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_No_Region_When_The_Field_Was_Never_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor")
            .Build();
        var rule = new RequiredFieldRule("Block", "block");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Null(result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Page_Of_The_Region_It_Chose()
    {
        // The label sits on page 1 and the value wraps onto page 2. The
        // reported page must follow the box that was reported.
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField(
                "project description",
                "Placing fill across the rear third of the lot",
                page: 1,
                keyBox: RegionOn(1, 0.1),
                valueBox: RegionOn(2, 0.1))
            .Build();
        var rule = new RequiredFieldRule("Project description", "project description");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(2, result.BoundingBox?.PageNumber);
        Assert.Equal(2, result.Page);
    }
}
