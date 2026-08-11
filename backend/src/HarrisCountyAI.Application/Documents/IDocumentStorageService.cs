namespace HarrisCountyAI.Application.Documents;

/// <summary>
/// Low-level blob storage operations for document content. Operations are
/// container-scoped and deal only in blob paths; callers build paths with
/// <see cref="DocumentBlobPathBuilder"/> so the same service can serve both
/// case documents and the knowledge-base corpus.
/// </summary>
public interface IDocumentStorageService
{
    /// <summary>
    /// Uploads <paramref name="content"/> to <paramref name="container"/> at
    /// <paramref name="blobPath"/>, overwriting any existing blob at that path.
    /// </summary>
    /// <returns>The blob path the content was stored under.</returns>
    Task<string> UploadAsync(
        DocumentStorageContainer container,
        string blobPath,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a readable stream over the blob at <paramref name="blobPath"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">The blob does not exist.</exception>
    Task<Stream> DownloadAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the blob at <paramref name="blobPath"/> if it exists.
    /// </summary>
    /// <returns><c>true</c> if a blob was deleted; <c>false</c> if none existed.</returns>
    Task<bool> DeleteAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a blob exists at <paramref name="blobPath"/>.
    /// </summary>
    Task<bool> ExistsAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default);
}
