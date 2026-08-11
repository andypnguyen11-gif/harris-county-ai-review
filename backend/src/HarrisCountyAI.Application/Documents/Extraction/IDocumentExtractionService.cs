namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>
/// Extracts text and structure (pages, key/value pairs, selection marks,
/// tables) from a document's raw content. Implementations live in
/// Infrastructure so the application layer stays free of vendor SDKs.
/// </summary>
public interface IDocumentExtractionService
{
    /// <summary>
    /// Analyzes <paramref name="content"/> and returns the extracted document
    /// structure attributed to <paramref name="documentId"/>.
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(Guid documentId, Stream content, CancellationToken cancellationToken);
}
