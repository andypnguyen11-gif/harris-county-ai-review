namespace HarrisCountyAI.Application.KnowledgeBase.Ingestion;

/// <summary>
/// Runs a knowledge document through the corpus ingestion pipeline:
/// download → extract → normalize → chunk → embed → index.
/// </summary>
public interface IKnowledgeDocumentIngestionService
{
    /// <summary>
    /// Ingests (or re-ingests) the document into the reference corpus index.
    /// Re-ingestion first deletes the document's existing chunks so the index
    /// never holds stale content.
    /// </summary>
    /// <returns>
    /// The pipeline outcome, or <c>null</c> when no document with
    /// <paramref name="documentId"/> exists. Stage failures are captured as a
    /// <see cref="IngestionStatus.Failed"/> result rather than thrown.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The document is deactivated or is already being processed.
    /// </exception>
    Task<IngestionResult?> IngestAsync(Guid documentId, CancellationToken cancellationToken = default);
}
