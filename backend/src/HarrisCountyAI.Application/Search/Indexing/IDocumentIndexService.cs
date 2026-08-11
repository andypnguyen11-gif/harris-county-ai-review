namespace HarrisCountyAI.Application.Search.Indexing;

/// <summary>
/// Writes document chunks to the search index that backs retrieval. Case
/// documents and the Harris County reference corpus share the index; the
/// <c>sourceType</c> and <c>caseId</c> fields on every chunk keep them
/// strictly filterable apart.
/// </summary>
public interface IDocumentIndexService
{
    /// <summary>
    /// Creates the search index if it does not exist, or updates its schema
    /// to the current definition. Safe to call repeatedly.
    /// </summary>
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads <paramref name="chunks"/> to the index, overwriting any
    /// existing chunks with the same chunk id. A no-op for an empty list.
    /// </summary>
    Task IndexAsync(IReadOnlyList<IndexableChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every indexed chunk belonging to
    /// <paramref name="documentId"/>. Used when a document is removed or is
    /// about to be re-indexed.
    /// </summary>
    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
