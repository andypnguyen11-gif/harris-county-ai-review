namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Configuration for semantic reranking, bound from the
/// <see cref="SectionName"/> configuration section. Every setting has a safe
/// default, so the section may be omitted entirely.
/// </summary>
/// <remarks>
/// Semantic ranking is an Azure AI Search service-tier capability that the
/// free tier lacks, so it defaults to disabled. When disabled (or when the
/// service rejects a semantic query), retrieval returns results in plain
/// hybrid order — the application never depends on reranking being available.
/// </remarks>
public sealed class RerankingOptions
{
    public const string SectionName = "Reranking";

    /// <summary>
    /// Whether retrieval reranks candidates with Azure semantic ranking.
    /// Requires a search service tier that supports semantic ranker; also
    /// controls whether the index carries the semantic configuration.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Name of the semantic configuration on the chunk index.</summary>
    public string SemanticConfigurationName { get; set; } = SearchIndexDefinition.SemanticConfigurationName;

    /// <summary>
    /// Number of hybrid-search candidates retrieved for reranking, between 1
    /// and 50 (the Azure semantic ranker rescores at most 50 results). The
    /// reranked list is then trimmed to the caller's requested TopK.
    /// </summary>
    public int CandidatePoolSize { get; set; } = 20;
}
