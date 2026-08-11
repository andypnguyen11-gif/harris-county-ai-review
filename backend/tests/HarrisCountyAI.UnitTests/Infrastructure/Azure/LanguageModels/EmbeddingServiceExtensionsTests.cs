using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Infrastructure.Azure.LanguageModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

public class EmbeddingServiceExtensionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddEmbeddingService(configuration);

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Embeddings:Endpoint"] = "https://unit-test.openai.azure.com/",
        ["Embeddings:ApiKey"] = "unit-test-key",
        ["Embeddings:Deployment"] = "text-embedding-3-small",
        ["Embeddings:MaxBatchSize"] = "8",
        ["Embeddings:TimeoutSeconds"] = "45",
    };

    [Fact]
    public void AddEmbeddingService_BindsOptionsFromEmbeddingsSection()
    {
        using var provider = BuildProvider(ValidSettings());

        var options = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

        Assert.Equal("https://unit-test.openai.azure.com/", options.Endpoint);
        Assert.Equal("unit-test-key", options.ApiKey);
        Assert.Equal("text-embedding-3-small", options.Deployment);
        Assert.Equal(8, options.MaxBatchSize);
        Assert.Equal(45, options.TimeoutSeconds);
    }

    [Fact]
    public void AddEmbeddingService_ResolvesAzureEmbeddingService()
    {
        using var provider = BuildProvider(ValidSettings());

        var service = provider.GetRequiredService<IEmbeddingService>();

        Assert.IsType<AzureEmbeddingService>(service);
    }

    [Fact]
    public void AddEmbeddingService_ResolvesAzureBackedBatchClient()
    {
        using var provider = BuildProvider(ValidSettings());

        var client = provider.GetRequiredService<IEmbeddingBatchClient>();

        Assert.IsType<AzureOpenAIEmbeddingBatchClient>(client);
    }

    [Fact]
    public void AddEmbeddingService_InvalidConfiguration_FailsOnOptionsAccess()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Embeddings:Endpoint"] = "https://unit-test.openai.azure.com/",
            // ApiKey and Deployment intentionally missing.
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value);

        Assert.Contains("ApiKey", exception.Message);
        Assert.Contains("Deployment", exception.Message);
    }
}
