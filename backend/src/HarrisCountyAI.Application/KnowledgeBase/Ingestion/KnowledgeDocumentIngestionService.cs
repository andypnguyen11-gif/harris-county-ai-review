using System.Diagnostics;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Search.Chunking;
using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.Application.KnowledgeBase.Ingestion;

/// <summary>
/// Default <see cref="IKnowledgeDocumentIngestionService"/>: marks the
/// document Processing, downloads it from the knowledge-base blob container,
/// extracts its text, normalizes the extracted pages, chunks them, embeds the
/// chunks, and writes the result to the search index tagged
/// <see cref="IndexSourceTypes.KnowledgeBase"/> — never blending it with case
/// documents. Re-ingestion deletes the document's existing chunks before
/// indexing so the index never serves stale content. Any stage failure marks
/// the document Failed (it can be reprocessed later) and is reported in the
/// returned <see cref="IngestionResult"/>.
/// </summary>
public sealed class KnowledgeDocumentIngestionService : IKnowledgeDocumentIngestionService
{
    private readonly IKnowledgeDocumentRepository _repository;
    private readonly IDocumentStorageService _storage;
    private readonly IDocumentExtractionService _extractionService;
    private readonly IDocumentChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentIndexService _indexService;
    private readonly ILogger<KnowledgeDocumentIngestionService> _logger;

    public KnowledgeDocumentIngestionService(
        IKnowledgeDocumentRepository repository,
        IDocumentStorageService storage,
        IDocumentExtractionService extractionService,
        IDocumentChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IDocumentIndexService indexService,
        ILogger<KnowledgeDocumentIngestionService> logger)
    {
        _repository = repository;
        _storage = storage;
        _extractionService = extractionService;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _indexService = indexService;
        _logger = logger;
    }

    public async Task<IngestionResult?> IngestAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _repository.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        // Throws when the document is deactivated or already processing;
        // callers surface that as a conflict rather than a pipeline failure.
        document.MarkProcessing();
        await _repository.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var extracted = await ExtractAsync(document, cancellationToken);
            _logger.LogInformation(
                "Extracted knowledge document {DocumentId} ({Title}): {PageCount} pages, {CharacterCount} characters.",
                document.Id, document.Title, extracted.Pages.Count, extracted.RawText.Length);

            var chunks = _chunkingService.ChunkDocument(Normalize(document, extracted));
            if (chunks.Count == 0)
            {
                return await FailAsync(document, "The document contains no extractable text to index.", null, stopwatch);
            }

            _logger.LogInformation(
                "Chunked knowledge document {DocumentId} into {ChunkCount} chunks.",
                document.Id, chunks.Count);

            var embeddings = await EmbedAsync(chunks, cancellationToken);
            _logger.LogInformation(
                "Embedded {ChunkCount} chunks for knowledge document {DocumentId}.",
                chunks.Count, document.Id);

            var indexableChunks = BuildIndexableChunks(document, chunks, embeddings);

            await _indexService.EnsureIndexAsync(cancellationToken);
            // Delete-then-index so re-ingestion never leaves stale chunks behind.
            await _indexService.DeleteDocumentAsync(document.Id, cancellationToken);
            await _indexService.IndexAsync(indexableChunks, cancellationToken);

            document.MarkIngested();
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Ingested knowledge document {DocumentId} ({Title}) in {ElapsedMilliseconds} ms: {ChunkCount} chunks indexed.",
                document.Id, document.Title, stopwatch.ElapsedMilliseconds, chunks.Count);

            return IngestionResult.Succeeded(document.Id, chunks.Count);
        }
        catch (OperationCanceledException)
        {
            // Record the failure so the document is not stuck in Processing,
            // then let the cancellation propagate.
            await MarkFailedAsync(document);
            throw;
        }
        catch (Exception exception)
        {
            return await FailAsync(document, exception.Message, exception, stopwatch);
        }
    }

    /// <summary>Downloads the document from the knowledge-base container and extracts its structure.</summary>
    private async Task<ExtractedDocument> ExtractAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        await using var content = await _storage.DownloadAsync(
            DocumentStorageContainer.KnowledgeBase,
            document.BlobPath,
            cancellationToken);

        return await _extractionService.ExtractAsync(document.Id, content, cancellationToken);
    }

    /// <summary>
    /// Normalizes the extracted output into a chunking request: pages keep
    /// their page numbers with line endings unified and blank pages dropped;
    /// documents extracted without page structure fall back to the raw text
    /// as a single unpaged span.
    /// </summary>
    private static ChunkingRequest Normalize(KnowledgeDocument document, ExtractedDocument extracted)
    {
        var pages = extracted.Pages
            .Where(page => !string.IsNullOrWhiteSpace(page.Text))
            .Select(page => new ChunkingPage
            {
                PageNumber = page.PageNumber,
                Text = NormalizeText(page.Text),
            })
            .ToList();

        if (pages.Count == 0 && !string.IsNullOrWhiteSpace(extracted.RawText))
        {
            pages = [new ChunkingPage { Text = NormalizeText(extracted.RawText) }];
        }

        return new ChunkingRequest
        {
            DocumentId = document.Id,
            Title = document.Title,
            Pages = pages,
        };
    }

    /// <summary>Unifies line endings and trims outer whitespace without disturbing line structure.</summary>
    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>Embeds every chunk and returns the vectors in chunk order.</summary>
    private async Task<float[][]> EmbedAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var inputs = chunks.Select(chunk => chunk.Text).ToList();
        var results = await _embeddingService.EmbedAsync(inputs, cancellationToken);

        if (results.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"The embedding service returned {results.Count} embeddings for {chunks.Count} chunks.");
        }

        var vectors = new float[chunks.Count][];
        foreach (var result in results)
        {
            if (result.InputIndex < 0 || result.InputIndex >= chunks.Count || vectors[result.InputIndex] is not null)
            {
                throw new InvalidOperationException(
                    $"The embedding service returned an invalid or duplicate input index {result.InputIndex}.");
            }

            vectors[result.InputIndex] = result.Vector;
        }

        return vectors;
    }

    /// <summary>
    /// Attaches embeddings and corpus metadata to the chunks. Every chunk is
    /// tagged <see cref="IndexSourceTypes.KnowledgeBase"/> with no case id,
    /// keeping the reference corpus strictly separate from case documents.
    /// </summary>
    private static List<IndexableChunk> BuildIndexableChunks(
        KnowledgeDocument document,
        IReadOnlyList<DocumentChunk> chunks,
        float[][] embeddings) =>
        chunks
            .Select((chunk, index) => IndexableChunk.FromChunk(
                chunk,
                embeddings[index],
                IndexSourceTypes.KnowledgeBase,
                document.Title,
                department: document.Department,
                permitType: document.PermitType,
                documentType: document.DocumentType,
                effectiveDate: ToEffectiveDate(document.EffectiveDate),
                sourceUrl: document.SourceUrl))
            .ToList();

    private static DateTimeOffset? ToEffectiveDate(DateOnly? effectiveDate) =>
        effectiveDate is { } date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

    /// <summary>Marks the document Failed, logs the stage error, and reports the failure.</summary>
    private async Task<IngestionResult> FailAsync(
        KnowledgeDocument document,
        string reason,
        Exception? exception,
        Stopwatch stopwatch)
    {
        await MarkFailedAsync(document);

        _logger.LogError(
            exception,
            "Ingesting knowledge document {DocumentId} ({Title}) failed after {ElapsedMilliseconds} ms: {Reason}",
            document.Id, document.Title, stopwatch.ElapsedMilliseconds, reason);

        return IngestionResult.Failed(document.Id, reason);
    }

    private async Task MarkFailedAsync(KnowledgeDocument document)
    {
        // Persist the failure even when the pipeline was cancelled.
        document.MarkFailed();
        await _repository.SaveChangesAsync(CancellationToken.None);
    }
}
