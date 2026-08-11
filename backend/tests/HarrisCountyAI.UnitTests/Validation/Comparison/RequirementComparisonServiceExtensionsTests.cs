using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Application.Validation.Comparison;
using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.UnitTests.Common.AI;
using HarrisCountyAI.UnitTests.QuestionAnswering;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.UnitTests.Validation.Comparison;

public class RequirementComparisonServiceExtensionsTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetrievalService>(new FakeRetrievalService());
        services.AddSingleton<ILanguageModelService>(new FakeLanguageModelService());
        services.AddSemanticValidation();
        return services;
    }

    [Fact]
    public void AddRequirementComparison_Registers_The_Service_And_Its_Catalog()
    {
        var services = BaseServices();
        services.AddRequirementComparison();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<RequirementComparisonService>(
            scope.ServiceProvider.GetRequiredService<IRequirementComparisonService>());
        Assert.IsType<FloodplainDevelopmentPermitRequirements>(
            Assert.Single(scope.ServiceProvider.GetServices<IRequirementCatalog>()));
    }

    [Fact]
    public void AddRequirementComparison_Is_Idempotent()
    {
        var services = BaseServices();
        services.AddRequirementComparison();
        services.AddRequirementComparison();

        Assert.Single(services, d => d.ServiceType == typeof(IRequirementComparisonService));
        Assert.Single(services, d => d.ServiceType == typeof(IRequirementCatalog));
    }

    [Fact]
    public void AddValidation_Also_Wires_The_Comparison_Engine()
    {
        // The engine shares the validation stack's dependencies, so it hangs
        // off the already-wired extension rather than a composition-root line.
        var services = BaseServices();
        services.AddValidation();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<RequirementComparisonService>(
            scope.ServiceProvider.GetRequiredService<IRequirementComparisonService>());
    }

    [Fact]
    public void A_Custom_Registration_Made_Before_The_Extension_Wins()
    {
        var services = BaseServices();
        services.AddScoped<IRequirementComparisonService, StubRequirementComparisonService>();
        services.AddRequirementComparison();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<StubRequirementComparisonService>(
            scope.ServiceProvider.GetRequiredService<IRequirementComparisonService>());
    }

    private sealed class StubRequirementComparisonService : IRequirementComparisonService
    {
        public Task<IReadOnlyList<RequirementComparisonResult>> CompareAsync(
            RequirementComparisonRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
