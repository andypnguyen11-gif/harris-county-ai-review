namespace HarrisCountyAI.IntegrationTests.Search;

/// <summary>
/// A fact that only runs when Azure AI Search credentials are present in the
/// environment (<c>SEARCH_ENDPOINT</c> plus <c>SEARCH_API_KEY</c> or
/// <c>SEARCH_ADMIN_KEY</c>). Skips cleanly otherwise so CI without Azure
/// access stays green.
/// </summary>
public sealed class AzureSearchFactAttribute : FactAttribute
{
    public AzureSearchFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEARCH_ENDPOINT"))
            || string.IsNullOrWhiteSpace(AzureSearchEnvironment.ApiKey))
        {
            Skip = "SEARCH_ENDPOINT / SEARCH_API_KEY are not configured; skipping Azure AI Search integration tests.";
        }
    }
}

/// <summary>Reads the Azure AI Search test configuration from the environment.</summary>
public static class AzureSearchEnvironment
{
    public static string? Endpoint => Environment.GetEnvironmentVariable("SEARCH_ENDPOINT");

    public static string? ApiKey =>
        Environment.GetEnvironmentVariable("SEARCH_API_KEY")
        ?? Environment.GetEnvironmentVariable("SEARCH_ADMIN_KEY");

    public static string IndexName =>
        Environment.GetEnvironmentVariable("SEARCH_INDEX_NAME") ?? "harris-county-chunks";
}
