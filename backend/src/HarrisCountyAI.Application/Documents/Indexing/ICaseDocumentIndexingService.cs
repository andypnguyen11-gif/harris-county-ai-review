namespace HarrisCountyAI.Application.Documents.Indexing;

/// <summary>
/// Makes one case's uploaded documents searchable: chunks the document's
/// normalized extracted text, embeds the chunks, and writes them to the shared
/// search index tagged <c>CaseDocument</c> with the owning case's id — never
/// as knowledge-base content. Re-indexing is delete-then-index, so the index
/// never serves stale chunks after a document is reprocessed.
/// </summary>
public interface ICaseDocumentIndexingService
{
    /// <summary>
    /// Indexes (or re-indexes) the document's normalized text. Returns null
    /// when the document or its normalized snapshot does not exist; otherwise
    /// reports how many chunks were indexed. A document whose snapshot
    /// contains no indexable text has its existing index records removed and
    /// reports zero chunks.
    /// </summary>
    Task<CaseDocumentIndexingResult?> IndexAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every indexed chunk of the document. Used when a document is
    /// removed from its case.
    /// </summary>
    Task RemoveAsync(Guid documentId, CancellationToken cancellationToken = default);
}
