using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.Search.Reranking;

/// <summary>
/// Reorders retrieved candidate chunks by semantic relevance to the query and
/// trims them to the best few, so a wide retrieval pool can be narrowed to a
/// focused context window.
/// </summary>
/// <remarks>
/// Implementations must fail open: when reranking is disabled or unavailable,
/// they return the leading candidates in their original retrieval order rather
/// than failing the retrieval pipeline. A reranked chunk carries its score in
/// <see cref="RetrievedChunk.RerankerScore"/>; a passed-through chunk leaves it
/// null.
/// </remarks>
public interface IRerankingService
{
    /// <summary>
    /// Returns at most <see cref="RerankingRequest.TopN"/> of the request's
    /// candidates, most relevant first.
    /// </summary>
    /// <param name="request">The query, the candidate chunks, and the number to keep.</param>
    /// <param name="cancellationToken">Cancels the in-flight reranking when signaled.</param>
    Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        RerankingRequest request,
        CancellationToken cancellationToken = default);
}
