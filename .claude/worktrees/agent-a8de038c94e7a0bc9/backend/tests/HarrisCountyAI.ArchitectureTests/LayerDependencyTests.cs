using NetArchTest.Rules;

namespace HarrisCountyAI.ArchitectureTests;

public class LayerDependencyTests
{
    private const string DomainNamespace = "HarrisCountyAI.Domain";
    private const string ApplicationNamespace = "HarrisCountyAI.Application";
    private const string InfrastructureNamespace = "HarrisCountyAI.Infrastructure";
    private const string ApiNamespace = "HarrisCountyAI.Api";

    [Fact]
    public void Domain_Should_Not_Depend_On_Other_Layers()
    {
        var result = Types.InAssembly(typeof(Domain.AssemblyReference).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Application_Should_Only_Depend_On_Domain()
    {
        var result = Types.InAssembly(typeof(Application.AssemblyReference).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Api()
    {
        var result = Types.InAssembly(typeof(Infrastructure.AssemblyReference).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    private static string FailureMessage(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
