namespace HarrisCountyAI.Application.Documents.ProcessDocument;

/// <summary>
/// Outcome of one run of the processing pipeline over an uploaded document.
/// </summary>
/// <remarks>
/// There is deliberately no separate run-status field: the document's own
/// <see cref="Domain.Enums.DocumentProcessingStatus"/> is the single source of
/// truth, so a caller reads the same field here as it does from
/// <c>GET /api/cases/{caseId}/documents/{documentId}</c>. A completed run
/// leaves the document <c>Normalized</c>; a failed run leaves it <c>Failed</c>
/// and populates <see cref="FailureReason"/>.
/// </remarks>
/// <param name="Document">The document as it stands after the run.</param>
/// <param name="FailureReason">Why the run failed; null when it succeeded.</param>
public sealed record ProcessDocumentResult(DocumentDto Document, string? FailureReason)
{
    public static ProcessDocumentResult Processed(DocumentDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(document, null);
    }

    public static ProcessDocumentResult Failed(DocumentDto document, string reason)
    {
        ArgumentNullException.ThrowIfNull(document);
        return string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A failed result requires a reason.", nameof(reason))
            : new(document, reason);
    }
}
