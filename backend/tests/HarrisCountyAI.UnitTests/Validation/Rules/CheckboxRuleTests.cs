using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Validation.Rules;

public class CheckboxRuleTests
{
    [Fact]
    public async Task Complete_When_Required_Checkbox_Checked()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("2006 IRC", isChecked: true, page: 1)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new CheckboxRule("Building code selection", "2006 IRC", documentType: DocumentType.PermitApplication);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal("2006 IRC", result.ExtractedValue);
        Assert.Equal(document.Id, result.SourceDocumentId);
        Assert.Equal(1, result.Page);
        Assert.Equal("CheckboxRule(Building code selection)", result.RuleName);
    }

    [Fact]
    public async Task Missing_When_Required_Checkbox_Present_But_Unchecked()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("2006 IRC", isChecked: false)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new CheckboxRule("Building code selection", "2006 IRC");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Contains("not checked", result.Message);
    }

    [Fact]
    public async Task Missing_When_Checkbox_Field_Not_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("ADDRESS", "123 Main St")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new CheckboxRule("Building code selection", "2006 IRC");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public async Task Complete_When_Any_Checkbox_In_Group_Checked()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("Single Family Dwelling", isChecked: false)
            .WithCheckbox("Swimming Pool", isChecked: true)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new CheckboxRule(
            "Construction type selection",
            [["Single Family Dwelling"], ["Swimming Pool"], ["Other"]],
            DocumentType.PermitApplication);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal("Swimming Pool", result.ExtractedValue);
    }

    [Fact]
    public async Task Missing_When_No_Checkbox_In_Group_Checked()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("Single Family Dwelling", isChecked: false)
            .WithCheckbox("Swimming Pool", isChecked: false)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new CheckboxRule(
            "Construction type selection",
            [["Single Family Dwelling"], ["Swimming Pool"]]);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Contains("None of the checkboxes", result.Message);
    }

    [Fact]
    public async Task Checkbox_Group_Matches_Name_Variants()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("MANUFACTURED HOME / MOBILE HOME", isChecked: true)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new CheckboxRule(
            "Construction type selection",
            [["Single Family Dwelling"], ["Manufactured Home/Mobile Home", "Mobile Home"]]);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
    }

    [Fact]
    public async Task Complete_NotApplicable_When_Condition_Not_Met()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("Fill", isChecked: false)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);
        var rule = new CheckboxRule(
            "Fill acknowledgement",
            "Fill Acknowledgement",
            applicableWhen: ctx => ctx.FindField(["Fill"]) is { Field.IsChecked: true });

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Contains("Not applicable", result.Message);
    }

    [Fact]
    public async Task UnableToDetermine_When_Scoped_Document_Type_Absent()
    {
        var context = NormalizedDocumentBuilder.ContextFor(
            new NormalizedDocumentBuilder(DocumentType.SitePlan).Build());
        var rule = new CheckboxRule("Building code selection", "2006 IRC", documentType: DocumentType.PermitApplication);

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
    }
}
