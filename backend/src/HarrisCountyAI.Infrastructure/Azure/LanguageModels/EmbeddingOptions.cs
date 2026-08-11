namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Configuration for the Azure OpenAI embedding service, bound from the
/// <c>Embeddings</c> configuration section.
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>The Azure OpenAI resource endpoint, e.g. <c>https://my-resource.openai.azure.com/</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The Azure OpenAI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The embedding deployment name, e.g. <c>text-embedding-3-small</c>.</summary>
    public string Deployment { get; set; } = string.Empty;

    /// <summary>Maximum number of inputs sent in a single embedding request.</summary>
    public int MaxBatchSize { get; set; } = 16;

    /// <summary>Network timeout applied to each embedding request.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
