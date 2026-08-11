namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>How corpus retrieval queries the chunk index.</summary>
public enum RetrievalMode
{
    /// <summary>
    /// Keyword search and vector search in one query, fused by the service
    /// with Reciprocal Rank Fusion. The default: keyword matching catches
    /// exact identifiers (section numbers, form numbers) that embeddings
    /// blur, while the vector leg catches paraphrased questions.
    /// </summary>
    Hybrid = 0,

    /// <summary>
    /// Vector similarity search only. Kept for A/B comparison against
    /// <see cref="Hybrid"/> (see docs/architecture/rag-architecture.md).
    /// </summary>
    VectorOnly = 1,
}
