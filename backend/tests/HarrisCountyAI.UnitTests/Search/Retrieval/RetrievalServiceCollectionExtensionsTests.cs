using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    public void AddCorpusRetrieval_Defaults_To_Hybrid_Retrieval()
    {
        var configuration = ValidConfiguration();
        var services = BaseServices(configuration);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RetrievalOptions>>().Value;

        Assert.Equal(RetrievalMode.Hybrid, options.Mode);
        Assert.Equal(RetrievalRequest.DefaultTopK, options.DefaultTopK);
    }

    [Fact]
    public void AddCorpusRetrieval_Binds_Retrieval_Options_From_Configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(ValidConfiguration())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retrieval:Mode"] = "VectorOnly",
                ["Retrieval:DefaultTopK"] = "8",
            })
            .Build();
        var services = BaseServices(configuration);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RetrievalOptions>>().Value;

        Assert.Equal(RetrievalMode.VectorOnly, options.Mode);
        Assert.Equal(8, options.DefaultTopK);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    public void AddCorpusRetrieval_Rejects_An_Out_Of_Range_Default_TopK(string defaultTopK)
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(ValidConfiguration())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retrieval:DefaultTopK"] = defaultTopK,
            })
            .Build();
        var services = BaseServices(configuration);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RetrievalOptions>>().Value);
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
