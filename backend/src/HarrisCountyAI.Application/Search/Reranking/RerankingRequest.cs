using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.Search.Reranking;

/// <summary>
/// A request to reorder retrieved candidates by relevance to a query and keep
/// only the best of them.
/// </summary>
public sealed record RerankingRequest
{
    /// <summary>The question the candidates were retrieved for.</summary>
    public required string Query { get; init; }

    /// <summary>Candidate chunks in their retrieval order, best first.</summary>
    public required IReadOnlyList<RetrievedChunk> Candidates { get; init; }

    /// <summary>Number of chunks to keep after reranking, at least 1.</summary>
    public required int TopN { get; init; }
}
