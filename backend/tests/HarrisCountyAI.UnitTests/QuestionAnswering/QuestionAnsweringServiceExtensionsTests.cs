using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.Common.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.UnitTests.QuestionAnswering;

public class QuestionAnsweringServiceExtensionsTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRetrievalService>(new FakeRetrievalService());
        services.AddSingleton<ILanguageModelService>(new FakeLanguageModelService());
        return services;
    }

    [Fact]
    public void AddQuestionAnswering_Registers_The_Service()
    {
        var services = BaseServices();
        services.AddQuestionAnswering();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IQuestionAnsweringService>();

        Assert.IsType<QuestionAnsweringService>(service);
    }

    [Fact]
    public void AddQuestionAnswering_Is_Idempotent()
    {
        var services = BaseServices();
        services.AddQuestionAnswering();
        services.AddQuestionAnswering();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IQuestionAnsweringService));
    }
}
