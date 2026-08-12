using HarrisCountyAI.Infrastructure.Azure.LanguageModels;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Builds the Azure-backed services a live evaluation run needs, from
/// environment configuration only.
/// </summary>
/// <remarks>
/// Configuration comes from the standard double-underscore environment form
/// (<c>Search__Endpoint</c>, <c>Embeddings__ApiKey</c>, <c>LanguageModel__Deployment</c>,
/// …), which is how the deployed application is configured too. Nothing is
/// read from a committed file and nothing is defaulted to a real value, so a
/// machine without credentials simply cannot start a live run — the
/// <see cref="LiveEvaluationFactAttribute"/> gate skips first.
/// </remarks>
public static class LiveEvaluationHost
{
    /// <summary>Environment settings a live retrieval run requires.</summary>
    public static readonly string[] RetrievalRequirements =
    [
        "Search__Endpoint",
        "Search__ApiKey",
        "Embeddings__Endpoint",
        "Embeddings__ApiKey",
    ];

    /// <summary>Environment settings a live generation or judge run requires, on top of retrieval.</summary>
    public static readonly string[] GenerationRequirements =
    [
        .. RetrievalRequirements,
        "LanguageModel__Endpoint",
        "LanguageModel__ApiKey",
    ];

    /// <summary>
    /// Builds a provider with corpus retrieval wired to the configured Azure AI
    /// Search and embedding deployments.
    /// </summary>
    public static ServiceProvider BuildRetrievalProvider()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();
        services.AddLogging();
        services.AddSearchIndexing(configuration);
        services.AddCorpusRetrieval(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>Reads configuration from the environment, with no committed fallbacks.</summary>
    public static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder().AddEnvironmentVariables().Build();

    /// <summary>Describes the configuration a report was produced under, without ever naming a key.</summary>
    public static string DescribeRetrievalConfiguration()
    {
        var mode = Environment.GetEnvironmentVariable("Retrieval__Mode") ?? "Hybrid";
        var reranking = Environment.GetEnvironmentVariable("Reranking__Enabled") ?? "false";
        var index = Environment.GetEnvironmentVariable("Search__IndexName") ?? "harris-county-chunks";
        return $"live Azure AI Search (index {index}, mode {mode}, reranking {reranking})";
    }
}
