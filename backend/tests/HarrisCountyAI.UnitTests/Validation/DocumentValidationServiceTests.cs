using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.UnitTests.Validation;

public class DocumentValidationServiceTests
{
    private sealed class ThrowingRule : IValidationRule
    {
        public string Name => "ThrowingRule(Broken requirement)";

        public Task<ValidationResult> ValidateAsync(ValidationContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private static ValidationContext Context() =>
        NormalizedDocumentBuilder.ContextFor(
            new NormalizedDocumentBuilder(DocumentType.PermitApplication)
                .WithTextField("Owner Name", "Jane Smith")
                .Build());

    [Fact]
    public async Task Aggregates_Results_From_All_Rules_In_Order()
    {
        var service = new DocumentValidationService();
        var rules = new IValidationRule[]
        {
            new RequiredFieldRule("Owner name", "Owner Name"),
            new RequiredFieldRule("Property address", "Address"),
            new RequiredDocumentRule(DocumentType.SitePlan, "Site plan"),
        };

        var results = await service.ValidateAsync(Context(), rules);

        Assert.Equal(3, results.Count);
        Assert.Equal(["Owner name", "Property address", "Site plan"], results.Select(r => r.Requirement));
        Assert.Equal(
            [ValidationStatus.Complete, ValidationStatus.Missing, ValidationStatus.Missing],
            results.Select(r => r.Status));
    }

    [Fact]
    public async Task Rule_Failure_Is_Isolated_And_Reported_As_UnableToDetermine()
    {
        var service = new DocumentValidationService();
        var rules = new IValidationRule[]
        {
            new RequiredFieldRule("Owner name", "Owner Name"),
            new ThrowingRule(),
            new RequiredDocumentRule(DocumentType.PermitApplication, "Development permit application"),
        };

        var results = await service.ValidateAsync(Context(), rules);

        Assert.Equal(3, results.Count);

        var failure = results[1];
        Assert.Equal(ValidationStatus.UnableToDetermine, failure.Status);
        Assert.Equal("ThrowingRule(Broken requirement)", failure.RuleName);
        Assert.Contains("boom", failure.Message);
        Assert.Equal(ValidationType.Deterministic, failure.ValidationType);

        Assert.Equal(ValidationStatus.Complete, results[0].Status);
        Assert.Equal(ValidationStatus.Complete, results[2].Status);
    }

    [Fact]
    public async Task Empty_Rule_Set_Returns_Empty_Results()
    {
        var service = new DocumentValidationService();

        var results = await service.ValidateAsync(Context(), []);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var service = new DocumentValidationService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ValidateAsync(Context(), [new RequiredFieldRule("Owner name", "Owner Name")], cancellation.Token));
    }
}
