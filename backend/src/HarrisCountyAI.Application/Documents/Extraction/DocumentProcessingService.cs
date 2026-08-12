using System.Diagnostics;
using HarrisCountyAI.Application.Documents.Indexing;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>
/// Default <see cref="IDocumentProcessingService"/>: marks the document
/// <see cref="DocumentProcessingStatus.Extracting"/>, downloads its content
/// from blob storage, runs extraction (marking the document
/// <see cref="DocumentProcessingStatus.Extracted"/>), normalizes and persists
/// the result (marking it <see cref="DocumentProcessingStatus.Normalized"/>),
/// or marks it <see cref="DocumentProcessingStatus.Failed"/> on error. When a
/// case-document indexing service is available, the normalized text is then
/// indexed for search; an indexing failure is logged but never fails the
/// processing pipeline, because validation and review only depend on the
/// persisted normalized snapshot.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorageService _documentStorage;
    private readonly IDocumentExtractionService _extractionService;
    private readonly IDocumentNormalizationService _normalizationService;
    private readonly INormalizedDocumentRepository _normalizedDocumentRepository;
    private readonly ILogger<DocumentProcessingService> _logger;
    private readonly ICaseDocumentIndexingService? _caseDocumentIndexing;

    public DocumentProcessingService(
        IDocumentRepository documentRepository,
        IDocumentStorageService documentStorage,
        IDocumentExtractionService extractionService,
        IDocumentNormalizationService normalizationService,
        INormalizedDocumentRepository normalizedDocumentRepository,
        ILogger<DocumentProcessingService> logger,
        ICaseDocumentIndexingService? caseDocumentIndexing = null)
    {
        _documentRepository = documentRepository;
        _documentStorage = documentStorage;
        _extractionService = extractionService;
        _normalizationService = normalizationService;
        _normalizedDocumentRepository = normalizedDocumentRepository;
        _logger = logger;
        _caseDocumentIndexing = caseDocumentIndexing;
    }

    public async Task<NormalizedDocument> ProcessAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document '{documentId}' was not found.");

        var stopwatch = Stopwatch.StartNew();
        await SetStatusAsync(document, DocumentProcessingStatus.Extracting, cancellationToken);

        _logger.LogInformation(
            "Extraction started for document {DocumentId} ({FileName}) in case {CaseId}.",
            document.Id,
            document.FileName,
            document.CaseId);

        try
        {
            var extracted = await ExtractAsync(document, cancellationToken);
            await SetStatusAsync(document, DocumentProcessingStatus.Extracted, cancellationToken);

            var normalized = _normalizationService.Normalize(document.CaseId, document.DocumentType, extracted);
            await _normalizedDocumentRepository.AddAsync(normalized, cancellationToken);
            await _normalizedDocumentRepository.SaveChangesAsync(cancellationToken);
            await SetStatusAsync(document, DocumentProcessingStatus.Normalized, cancellationToken);

            await IndexForSearchAsync(document, cancellationToken);

            _logger.LogInformation(
                "Processed document {DocumentId} ({FileName}) in {ElapsedMilliseconds} ms: {PageCount} pages, {FieldCount} fields.",
                document.Id,
                document.FileName,
                stopwatch.ElapsedMilliseconds,
                normalized.Pages.Count,
                normalized.Fields.Count);

            return normalized;
        }
        catch (Exception exception)
        {
            // Record the failure even when the pipeline was cancelled.
            await SetStatusAsync(document, DocumentProcessingStatus.Failed, CancellationToken.None);

            _logger.LogError(
                exception,
                "Processing document {DocumentId} ({FileName}) failed after {ElapsedMilliseconds} ms.",
                document.Id,
                document.FileName,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    /// <summary>
    /// Indexes the document's normalized text for search. Deliberately
    /// fail-open: the snapshot is already persisted and validation must keep
    /// working when the search service is unavailable, so an indexing failure
    /// is logged and swallowed. Reprocessing the document re-indexes it.
    /// </summary>
    private async Task IndexForSearchAsync(Document document, CancellationToken cancellationToken)
    {
        if (_caseDocumentIndexing is null)
        {
            return;
        }

        try
        {
            await _caseDocumentIndexing.IndexAsync(document.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Indexing case document {DocumentId} ({FileName}) for search failed; "
                + "the document remains processed and can be re-indexed by reprocessing it.",
                document.Id,
                document.FileName);
        }
    }

    /// <summary>Downloads the document content and runs extraction over it.</summary>
    protected async Task<ExtractedDocument> ExtractAsync(Document document, CancellationToken cancellationToken)
    {
        await using var content = await _documentStorage.DownloadAsync(
            DocumentStorageContainer.CaseDocuments,
            document.BlobPath,
            cancellationToken);

        return await _extractionService.ExtractAsync(document.Id, content, cancellationToken);
    }

    private async Task SetStatusAsync(Document document, DocumentProcessingStatus status, CancellationToken cancellationToken)
    {
        document.SetProcessingStatus(status);
        await _documentRepository.SaveChangesAsync(cancellationToken);
    }
}
