using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Validation.Rules;

public class RequiredDocumentRuleTests
{
    [Fact]
    public async Task Complete_When_Document_Type_Present()
    {
        var sitePlan = new NormalizedDocumentBuilder(DocumentType.SitePlan).Build();
        var context = NormalizedDocumentBuilder.ContextFor(sitePlan);
        var rule = new RequiredDocumentRule(DocumentType.SitePlan, "Site plan");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal("Site plan", result.Requirement);
        Assert.Equal(sitePlan.Id, result.SourceDocumentId);
        Assert.Equal("RequiredDocumentRule(Site plan)", result.RuleName);
    }

    [Fact]
    public async Task Missing_When_Document_Type_Absent()
    {
        var application = new NormalizedDocumentBuilder(DocumentType.PermitApplication).Build();
        var context = NormalizedDocumentBuilder.ContextFor(application);
        var rule = new RequiredDocumentRule(DocumentType.SitePlan, "Site plan");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Null(result.SourceDocumentId);
        Assert.Contains("SitePlan", result.Message);
    }

    [Fact]
    public async Task Missing_Status_And_Message_Are_Configurable()
    {
        var context = NormalizedDocumentBuilder.ContextFor(
            new NormalizedDocumentBuilder(DocumentType.PermitApplication).Build());
        var rule = new RequiredDocumentRule(
            DocumentType.ElevationCertificate,
            "FEMA Elevation Certificate",
            missingStatus: ValidationStatus.NeedsHumanReview,
            missingMessage: "Required only for Class II submissions; reviewer must confirm.");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.NeedsHumanReview, result.Status);
        Assert.Equal("Required only for Class II submissions; reviewer must confirm.", result.Message);
    }

    [Fact]
    public async Task Complete_Reports_Count_When_Multiple_Documents_Present()
    {
        var first = new NormalizedDocumentBuilder(DocumentType.ElevationCertificate).Build();
        var second = new NormalizedDocumentBuilder(DocumentType.ElevationCertificate).Build();
        var context = NormalizedDocumentBuilder.ContextFor(first, second);
        var rule = new RequiredDocumentRule(DocumentType.ElevationCertificate, "FEMA Elevation Certificate");

        var result = await rule.ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(first.Id, result.SourceDocumentId);
        Assert.Contains("2", result.Message);
    }
}
