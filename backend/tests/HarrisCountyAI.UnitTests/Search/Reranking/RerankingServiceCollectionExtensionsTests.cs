using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Search.Reranking;

public class RerankingServiceCollectionExtensionsTests
{
    private static IConfiguration Configuration(Dictionary<string, string?>? values = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:Endpoint"] = "https://unit-test.search.windows.net",
                ["Search:ApiKey"] = "unit-test-key",
                ["Search:IndexName"] = "unit-test-index",
                ["Embeddings:Endpoint"] = "https://unit-test.openai.azure.com/",
                ["Embeddings:ApiKey"] = "unit-test-key",
                ["Embeddings:Deployment"] = "text-embedding-3-small",
            })
            .AddInMemoryCollection(values ?? [])
            .Build();

    private static ServiceCollection BaseServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSearchIndexing(configuration);
        return services;
    }

    [Fact]
    public void AddSemanticReranking_Registers_The_Azure_Reranking_Service()
    {
        var configuration = Configuration();
        var services = BaseServices(configuration);
        services.AddSemanticReranking(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<AzureSemanticRerankingService>(provider.GetRequiredService<IRerankingService>());
    }

    [Fact]
    public void AddSemanticReranking_Defaults_To_Disabled_With_Safe_Settings()
    {
        var configuration = Configuration();
        var services = BaseServices(configuration);
        services.AddSemanticReranking(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RerankingOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.Equal(SearchIndexDefinition.SemanticConfigurationName, options.SemanticConfigurationName);
        Assert.Equal(20, options.CandidatePoolSize);
    }

    [Fact]
    public void AddSemanticReranking_Binds_Options_From_Configuration()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Reranking:Enabled"] = "true",
            ["Reranking:SemanticConfigurationName"] = "custom-semantic",
            ["Reranking:CandidatePoolSize"] = "30",
        });
        var services = BaseServices(configuration);
        services.AddSemanticReranking(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RerankingOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal("custom-semantic", options.SemanticConfigurationName);
        Assert.Equal(30, options.CandidatePoolSize);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    public void AddSemanticReranking_Rejects_An_Out_Of_Range_Candidate_Pool_Size(string poolSize)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Reranking:CandidatePoolSize"] = poolSize,
        });
        var services = BaseServices(configuration);
        services.AddSemanticReranking(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RerankingOptions>>().Value);
    }

    [Fact]
    public void AddSemanticReranking_Requires_A_Configuration_Name_When_Enabled()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Reranking:Enabled"] = "true",
            ["Reranking:SemanticConfigurationName"] = "",
        });
        var services = BaseServices(configuration);
        services.AddSemanticReranking(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RerankingOptions>>().Value);
    }

    [Fact]
    public void AddSemanticReranking_Is_Idempotent()
    {
        var configuration = Configuration();
        var services = BaseServices(configuration);
        services.AddSemanticReranking(configuration);
        services.AddSemanticReranking(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRerankingService));
        Assert.NotNull(provider.GetRequiredService<IRerankingService>());
    }

    [Fact]
    public void AddCorpusRetrieval_Registers_Semantic_Reranking()
    {
        var configuration = Configuration();
        var services = BaseServices(configuration);
        services.AddCorpusRetrieval(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<AzureSemanticRerankingService>(provider.GetRequiredService<IRerankingService>());
    }
}
