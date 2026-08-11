namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Configuration for the Azure AI Search service, bound from the
/// <see cref="SectionName"/> configuration section.
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>Search service endpoint, e.g. <c>https://my-service.search.windows.net</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Admin API key for the search service.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Name of the chunk index.</summary>
    public string IndexName { get; set; } = "harris-county-chunks";
}
