namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// A single query against the chunk index, expressed independently of the
/// Azure SDK so retrieval logic can be unit tested through
/// <see cref="ISearchQueryGateway"/>.
/// </summary>
public sealed record ChunkSearchQuery
{
    /// <summary>
    /// Keyword search text. Null issues a pure vector query; set alongside
    /// <see cref="Vector"/> it produces a hybrid query (keyword and vector
    /// results fused by the service).
    /// </summary>
    public string? SearchText { get; init; }

    /// <summary>Query embedding for vector similarity search, or null for keyword-only.</summary>
    public float[]? Vector { get; init; }

    /// <summary>
    /// OData filter every result must satisfy. Required: no query path may
    /// search the shared chunk index unfiltered (see
    /// docs/architecture/rag-architecture.md).
    /// </summary>
    public required string Filter { get; init; }

    /// <summary>Maximum number of results to return.</summary>
    public required int Size { get; init; }

    /// <summary>
    /// When true, the service reranks results with its semantic ranker and
    /// reports reranker scores. Requires <see cref="SearchText"/> and a
    /// <see cref="SemanticConfigurationName"/>, and a service tier that
    /// supports semantic ranking.
    /// </summary>
    public bool UseSemanticRanking { get; init; }

    /// <summary>Semantic configuration to rank with; required when <see cref="UseSemanticRanking"/> is true.</summary>
    public string? SemanticConfigurationName { get; init; }
}
