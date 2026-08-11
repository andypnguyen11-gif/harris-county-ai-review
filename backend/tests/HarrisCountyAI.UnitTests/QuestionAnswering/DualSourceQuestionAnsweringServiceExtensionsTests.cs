using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.Common.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.UnitTests.QuestionAnswering;

public class DualSourceQuestionAnsweringServiceExtensionsTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetrievalService>(new FakeRetrievalService());
        services.AddSingleton<ILanguageModelService>(new FakeLanguageModelService());
        return services;
    }

    [Fact]
    public void AddDualSourceQuestionAnswering_Registers_The_Service()
    {
        var services = BaseServices();
        services.AddDualSourceQuestionAnswering();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IDualSourceQuestionAnsweringService>();

        Assert.IsType<DualSourceQuestionAnsweringService>(service);
    }

    [Fact]
    public void AddDualSourceQuestionAnswering_Is_Idempotent()
    {
        var services = BaseServices();
        services.AddDualSourceQuestionAnswering();
        services.AddDualSourceQuestionAnswering();

        Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IDualSourceQuestionAnsweringService));
    }

    [Fact]
    public void AddQuestionAnswering_Also_Wires_The_Dual_Source_Path()
    {
        // The dual-source service shares the single-scope path's dependencies,
        // so it hangs off the already-wired extension rather than needing its
        // own composition-root registration.
        var services = BaseServices();
        services.AddQuestionAnswering();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<DualSourceQuestionAnsweringService>(
            scope.ServiceProvider.GetRequiredService<IDualSourceQuestionAnsweringService>());
    }

    [Fact]
    public void A_Custom_Registration_Made_Before_The_Extension_Wins()
    {
        var services = BaseServices();
        services.AddScoped<IDualSourceQuestionAnsweringService, StubDualSourceQuestionAnsweringService>();
        services.AddDualSourceQuestionAnswering();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<StubDualSourceQuestionAnsweringService>(
            scope.ServiceProvider.GetRequiredService<IDualSourceQuestionAnsweringService>());
    }

    private sealed class StubDualSourceQuestionAnsweringService : IDualSourceQuestionAnsweringService
    {
        public Task<DualSourceQuestionResponse> CompareAsync(
            DualSourceQuestionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
