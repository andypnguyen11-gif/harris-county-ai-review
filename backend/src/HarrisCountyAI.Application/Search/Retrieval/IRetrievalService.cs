namespace HarrisCountyAI.Application.Search.Retrieval;

/// <summary>
/// Retrieves relevant passages from the Harris County reference corpus.
/// </summary>
/// <remarks>
/// This service is corpus-scoped by contract: every query it issues must
/// filter to knowledge-base chunks (<c>sourceType eq 'KnowledgeBase'</c>), so
/// case-uploaded documents can never appear in its results. Case evidence is
/// retrieved by a separate, case-scoped path — the two corpora share an index
/// but are never blended (see docs/architecture/rag-architecture.md).
/// </remarks>
public interface IRetrievalService
{
    /// <summary>
    /// Returns the corpus chunks most relevant to the request's query,
    /// most relevant first.
    /// </summary>
    /// <param name="request">The query, result count, and optional metadata filters.</param>
    /// <param name="cancellationToken">Cancels the in-flight retrieval when signaled.</param>
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default);
}
