using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.UnitTests.Common.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.UnitTests.Validation.Semantic;

public class SemanticValidationServiceExtensionsTests
{
    [Fact]
    public void AddSemanticValidation_Registers_The_Service_As_Scoped()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILanguageModelService>(new FakeLanguageModelService());

        services.AddSemanticValidation();

        var descriptor = Assert.Single(
            services, d => d.ServiceType == typeof(ISemanticValidationService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<SemanticValidationService>(
            scope.ServiceProvider.GetRequiredService<ISemanticValidationService>());
    }

    [Fact]
    public void AddSemanticValidation_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILanguageModelService>(new FakeLanguageModelService());

        services.AddSemanticValidation().AddSemanticValidation();

        Assert.Single(services, d => d.ServiceType == typeof(ISemanticValidationService));
    }
}
