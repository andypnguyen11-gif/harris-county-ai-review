namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// Configuration for Azure AI Document Intelligence, bound from the
/// <see cref="SectionName"/> configuration section.
/// </summary>
public sealed class DocumentIntelligenceOptions
{
    public const string SectionName = "DocumentIntelligence";

    /// <summary>Resource endpoint, e.g. "https://my-resource.cognitiveservices.azure.com/".</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Analysis model to run. The layout model extracts text, paragraphs,
    /// tables, and selection marks, and supports the key/value pairs add-on.
    /// </summary>
    public string ModelId { get; set; } = "prebuilt-layout";

    /// <summary>Maximum time to wait for a single analysis round trip. Defaults to 2 minutes.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
