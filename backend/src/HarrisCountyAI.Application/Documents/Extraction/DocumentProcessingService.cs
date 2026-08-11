using System.Diagnostics;
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
/// or marks it <see cref="DocumentProcessingStatus.Failed"/> on error.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorageService _documentStorage;
    private readonly IDocumentExtractionService _extractionService;
    private readonly IDocumentNormalizationService _normalizationService;
    private readonly INormalizedDocumentRepository _normalizedDocumentRepository;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        IDocumentRepository documentRepository,
        IDocumentStorageService documentStorage,
        IDocumentExtractionService extractionService,
        IDocumentNormalizationService normalizationService,
        INormalizedDocumentRepository normalizedDocumentRepository,
        ILogger<DocumentProcessingService> logger)
    {
        _documentRepository = documentRepository;
        _documentStorage = documentStorage;
        _extractionService = extractionService;
        _normalizationService = normalizationService;
        _normalizedDocumentRepository = normalizedDocumentRepository;
        _logger = logger;
    }

    public async Task<NormalizedDocument> ProcessAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document '{documentId}' was not found.");

        var stopwatch = Stopwatch.StartNew();
        await SetStatusAsync(document, DocumentProcessingStatus.Extracting, cancellationToken);

        try
        {
            var extracted = await ExtractAsync(document, cancellationToken);
            await SetStatusAsync(document, DocumentProcessingStatus.Extracted, cancellationToken);

            var normalized = _normalizationService.Normalize(document.CaseId, document.DocumentType, extracted);
            await _normalizedDocumentRepository.AddAsync(normalized, cancellationToken);
            await _normalizedDocumentRepository.SaveChangesAsync(cancellationToken);
            await SetStatusAsync(document, DocumentProcessingStatus.Normalized, cancellationToken);

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
