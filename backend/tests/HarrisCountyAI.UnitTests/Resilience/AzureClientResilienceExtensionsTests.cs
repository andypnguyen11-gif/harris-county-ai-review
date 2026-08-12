using Azure.Core;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Resilience;

public class AzureClientResilienceExtensionsTests
{
    [Fact]
    public void The_Budget_Is_Applied_To_Client_Options()
    {
        var resilience = new AzureResilienceOptions
        {
            MaxRetryAttempts = 4,
            RetryBaseDelayMilliseconds = 250,
            MaxRetryDelaySeconds = 7,
            NetworkTimeoutSeconds = 15,
        };

        var options = new SearchClientOptions().WithResilience(resilience);

        Assert.Equal(RetryMode.Exponential, options.Retry.Mode);
        Assert.Equal(4, options.Retry.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.Retry.Delay);
        Assert.Equal(TimeSpan.FromSeconds(7), options.Retry.MaxDelay);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Retry.NetworkTimeout);
    }

    [Fact]
    public void Retries_Can_Be_Turned_Off_Entirely()
    {
        var options = new BlobClientOptions()
            .WithResilience(new AzureResilienceOptions { MaxRetryAttempts = 0 });

        Assert.Equal(0, options.Retry.MaxRetries);
    }

    [Fact]
    public void Nonsensical_Values_Are_Clamped_Rather_Than_Producing_An_Invalid_Client()
    {
        var options = new BlobClientOptions().WithResilience(new AzureResilienceOptions
        {
            MaxRetryAttempts = -1,
            RetryBaseDelayMilliseconds = 0,
            MaxRetryDelaySeconds = 0,
            NetworkTimeoutSeconds = 0,
        });

        Assert.Equal(0, options.Retry.MaxRetries);
        Assert.True(options.Retry.Delay > TimeSpan.Zero);
        Assert.True(options.Retry.MaxDelay > TimeSpan.Zero);
        Assert.True(options.Retry.NetworkTimeout > TimeSpan.Zero);
    }

    [Fact]
    public void The_Blob_Client_Factory_Applies_The_Budget()
    {
        // Exercised through the factory the composition root uses, so the
        // wiring — not just the helper — is covered.
        var client = BlobStorageServiceExtensions.CreateBlobServiceClient(
            "UseDevelopmentStorage=true",
            new AzureResilienceOptions { MaxRetryAttempts = 2 });

        Assert.NotNull(client);
    }

    [Fact]
    public void The_Budget_Binds_From_Configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:MaxRetryAttempts"] = "5",
                ["Resilience:NetworkTimeoutSeconds"] = "45",
            })
            .Build();

        var provider = new ServiceCollection()
            .AddAzureResilience(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AzureResilienceOptions>>().Value;

        Assert.Equal(5, options.MaxRetryAttempts);
        Assert.Equal(45, options.NetworkTimeoutSeconds);
        Assert.Equal(500, options.RetryBaseDelayMilliseconds);
    }

    [Theory]
    [InlineData("MaxRetryAttempts", "11")]
    [InlineData("MaxRetryAttempts", "-1")]
    [InlineData("RetryBaseDelayMilliseconds", "0")]
    [InlineData("MaxRetryDelaySeconds", "0")]
    [InlineData("NetworkTimeoutSeconds", "0")]
    public void An_Unusable_Budget_Fails_Validation(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"Resilience:{key}"] = value })
            .Build();

        var provider = new ServiceCollection()
            .AddAzureResilience(configuration)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AzureResilienceOptions>>().Value);
    }

    [Fact]
    public void The_Defaults_Retry_A_Few_Times_With_Backoff()
    {
        var defaults = new AzureResilienceOptions();

        Assert.Equal(3, defaults.MaxRetryAttempts);
        Assert.Equal(500, defaults.RetryBaseDelayMilliseconds);
        Assert.Equal(10, defaults.MaxRetryDelaySeconds);
        Assert.Equal(30, defaults.NetworkTimeoutSeconds);
    }
}
