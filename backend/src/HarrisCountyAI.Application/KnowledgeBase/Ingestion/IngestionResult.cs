namespace HarrisCountyAI.Application.KnowledgeBase.Ingestion;

/// <summary>
/// Result of running the ingestion pipeline over a knowledge document.
/// </summary>
/// <param name="DocumentId">The knowledge document the pipeline ran over.</param>
/// <param name="Status">Whether the run succeeded or failed.</param>
/// <param name="ChunkCount">Number of chunks written to the search index; zero on failure.</param>
/// <param name="FailureReason">Human-readable reason the run failed; null on success.</param>
public sealed record IngestionResult(
    Guid DocumentId,
    IngestionStatus Status,
    int ChunkCount,
    string? FailureReason)
{
    public static IngestionResult Succeeded(Guid documentId, int chunkCount) =>
        new(documentId, IngestionStatus.Succeeded, chunkCount, null);

    public static IngestionResult Failed(Guid documentId, string reason) =>
        new(documentId, IngestionStatus.Failed, 0, reason);
}
