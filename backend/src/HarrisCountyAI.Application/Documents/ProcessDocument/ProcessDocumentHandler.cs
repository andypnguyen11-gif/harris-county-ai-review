using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Documents.ProcessDocument;

/// <summary>
/// Runs the extraction pipeline over a document a reviewer has already
/// uploaded, scoped to the case the document belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Processing is a separate step from upload on purpose. Upload only has to
/// store bytes and is fast; processing calls Azure Document Intelligence and
/// the embedding and index path, so it is slow and fails for reasons that have
/// nothing to do with the upload. Keeping them apart means a failure here never
/// costs the reviewer the stored file, and the same request retries the
/// expensive half alone.
/// </para>
/// <para>
/// <see cref="IDocumentProcessingService"/> records the terminal
/// <see cref="DocumentProcessingStatus.Failed"/> status and rethrows. This
/// handler turns that throw into a reported outcome so the caller learns both
/// that the run failed and why, rather than getting an opaque server error.
/// </para>
/// </remarks>
public sealed class ProcessDocumentHandler
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentProcessingService _processingService;
    private readonly ILogger<ProcessDocumentHandler> _logger;

    public ProcessDocumentHandler(
        IDocumentRepository documentRepository,
        IDocumentProcessingService processingService,
        ILogger<ProcessDocumentHandler>? logger = null)
    {
        _documentRepository = documentRepository;
        _processingService = processingService;
        _logger = logger ?? NullLogger<ProcessDocumentHandler>.Instance;
    }

    /// <summary>
    /// Processes the document, or returns null when it does not exist or does
    /// not belong to <paramref name="caseId"/>. Both cases are one answer on
    /// purpose, so a document id alone never reveals another case's contents.
    /// </summary>
    public async Task<ProcessDocumentResult?> HandleAsync(
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(caseId, documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        try
        {
            await _processingService.ProcessAsync(documentId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away. The pipeline has already recorded Failed,
            // so the document is not left stuck mid-run; there is nobody to
            // report the outcome to.
            throw;
        }
        catch (Exception exception)
        {
            // The pipeline logs the failure with its own context; this records
            // that it was reported to a caller as a completed-but-failed run.
            _logger.LogWarning(
                exception,
                "Processing document {DocumentId} in case {CaseId} failed; reporting the failure to the caller.",
                documentId,
                caseId);

            return ProcessDocumentResult.Failed(
                DocumentDto.FromEntity(await ReloadAsync(caseId, documentId, document)),
                exception.Message);
        }

        return ProcessDocumentResult.Processed(
            DocumentDto.FromEntity(await ReloadAsync(caseId, documentId, document, cancellationToken)));
    }

    /// <summary>
    /// Re-reads the document so the reported status is the one the pipeline
    /// persisted, falling back to the instance already in hand.
    /// </summary>
    private async Task<Domain.Entities.Document> ReloadAsync(
        Guid caseId,
        Guid documentId,
        Domain.Entities.Document fallback,
        CancellationToken cancellationToken = default)
        => await _documentRepository.GetByIdAsync(caseId, documentId, cancellationToken) ?? fallback;
}
