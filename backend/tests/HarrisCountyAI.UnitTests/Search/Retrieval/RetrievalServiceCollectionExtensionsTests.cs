using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.UnitTests.Search.Retrieval;

public class RetrievalServiceCollectionExtensionsTests
{
    private static IConfiguration ValidConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Search:Endpoint"] = "https://unit-test.search.windows.net",
            ["Search:ApiKey"] = "unit-test-key",
            ["Search:IndexName"] = "unit-test-index",
            ["Embeddings:Endpoint"] = "https://unit-test.openai.azure.com/",
            ["Embeddings:ApiKey"] = "unit-test-key",
            ["Embeddings:Deployment"] = "text-embedding-3-small",
        })
        .Build();

    private static ServiceCollection BaseServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSearchIndexing(configuration);
        return services;
    }

    [Fact]
    public void AddCorpusRetrieval_Registers_The_Azure_Retrieval_Service()
    {
        var configuration = ValidConfiguration();
        var services = BaseServices(configuration);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<IRetrievalService>();

        Assert.IsType<AzureRetrievalService>(service);
        Assert.IsType<AzureSearchQueryGateway>(provider.GetRequiredService<ISearchQueryGateway>());
    }

    [Fact]
    public void AddCorpusRetrieval_Registers_The_Embedding_Service_It_Depends_On()
    {
        var configuration = ValidConfiguration();
        var services = BaseServices(configuration);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IEmbeddingService>());
    }

    [Fact]
    public void AddCorpusRetrieval_Does_Not_Override_An_Existing_Embedding_Registration()
    {
        var configuration = ValidConfiguration();
        var services = BaseServices(configuration);
        var existing = new FakeEmbeddingService();
        services.AddSingleton<IEmbeddingService>(existing);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Same(existing, provider.GetRequiredService<IEmbeddingService>());
    }

    [Fact]
    public void AddCorpusRetrieval_Is_Idempotent()
    {
        var configuration = ValidConfiguration();
        var services = BaseServices(configuration);
        services.AddCorpusRetrieval(configuration);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRetrievalService));
        Assert.NotNull(provider.GetRequiredService<IRetrievalService>());
    }
}
