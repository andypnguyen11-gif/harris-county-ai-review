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
}
