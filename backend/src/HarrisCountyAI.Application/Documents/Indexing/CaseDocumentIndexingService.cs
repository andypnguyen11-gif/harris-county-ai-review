using System.Diagnostics;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Application.Search.Chunking;
using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Documents.Indexing;

/// <summary>
/// Default <see cref="ICaseDocumentIndexingService"/>: reads the document's
/// persisted normalized snapshot (produced by the extraction pipeline — no
/// re-extraction), chunks it, embeds the chunks, and writes them to the search
/// index tagged <see cref="IndexSourceTypes.CaseDocument"/> with the owning
/// case's id. Indexing is delete-then-index so reprocessing a document never
/// leaves stale chunks behind, and a snapshot with no indexable text results
/// in the document's chunks being removed from the index entirely.
/// </summary>
/// <remarks>
/// Every chunk this service writes carries
/// <c>sourceType = CaseDocument</c> and a non-null <c>caseId</c>. That tagging
/// is what keeps case evidence out of corpus retrieval (which filters
/// <c>sourceType eq 'KnowledgeBase'</c>) and scoped to its own case in
/// case retrieval — see docs/architecture/rag-architecture.md.
/// </remarks>
public sealed class CaseDocumentIndexingService : ICaseDocumentIndexingService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly INormalizedDocumentRepository _normalizedDocumentRepository;
    private readonly IDocumentChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentIndexService _indexService;
    private readonly ILogger<CaseDocumentIndexingService> _logger;

    public CaseDocumentIndexingService(
        IDocumentRepository documentRepository,
        INormalizedDocumentRepository normalizedDocumentRepository,
        IDocumentChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IDocumentIndexService indexService,
        ILogger<CaseDocumentIndexingService>? logger = null)
    {
        _documentRepository = documentRepository;
        _normalizedDocumentRepository = normalizedDocumentRepository;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _indexService = indexService;
        _logger = logger ?? NullLogger<CaseDocumentIndexingService>.Instance;
    }

    public async Task<CaseDocumentIndexingResult?> IndexAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            _logger.LogWarning("Case document {DocumentId} was not found; nothing to index.", documentId);
            return null;
        }

        var normalized = await _normalizedDocumentRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (normalized is null)
        {
            _logger.LogWarning(
                "Case document {DocumentId} has no normalized snapshot; nothing to index.", documentId);
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var chunks = _chunkingService.ChunkDocument(BuildChunkingRequest(document, normalized));

        await _indexService.EnsureIndexAsync(cancellationToken);
        // Delete-then-index: reprocessing a document must replace its chunks,
        // and a document that no longer yields text must disappear from search.
        await _indexService.DeleteDocumentAsync(document.Id, cancellationToken);

        if (chunks.Count == 0)
        {
            _logger.LogInformation(
                "Case document {DocumentId} ({FileName}) contains no indexable text; existing chunks removed.",
                document.Id, document.FileName);
            return Result(document, 0);
        }

        var embeddings = await EmbedAsync(chunks, cancellationToken);
        await _indexService.IndexAsync(BuildIndexableChunks(document, chunks, embeddings), cancellationToken);

        _logger.LogInformation(
            "Indexed case document {DocumentId} ({FileName}) for case {CaseId} in {ElapsedMilliseconds} ms: "
            + "{ChunkCount} chunks.",
            document.Id, document.FileName, document.CaseId, stopwatch.ElapsedMilliseconds, chunks.Count);

        return Result(document, chunks.Count);
    }

    public async Task RemoveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await _indexService.DeleteDocumentAsync(documentId, cancellationToken);
        _logger.LogInformation("Removed indexed chunks of case document {DocumentId}.", documentId);
    }

    /// <summary>
    /// Builds the chunking request from the normalized snapshot: pages keep
    /// their page numbers with blank pages dropped; a snapshot without page
    /// structure falls back to its raw text as one unpaged span.
    /// </summary>
    private static ChunkingRequest BuildChunkingRequest(Document document, NormalizedDocument normalized)
    {
        var pages = normalized.Pages
            .Where(page => !string.IsNullOrWhiteSpace(page.Text))
            .OrderBy(page => page.PageNumber)
            .Select(page => new ChunkingPage
            {
                PageNumber = page.PageNumber,
                Text = page.Text,
            })
            .ToList();

        if (pages.Count == 0 && !string.IsNullOrWhiteSpace(normalized.RawText))
        {
            pages = [new ChunkingPage { Text = normalized.RawText }];
        }

        return new ChunkingRequest
        {
            DocumentId = document.Id,
            Title = document.FileName,
            Pages = pages,
        };
    }

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
    /// Attaches embeddings and case metadata to the chunks. Every chunk is
    /// tagged <see cref="IndexSourceTypes.CaseDocument"/> with the owning
    /// case's id, keeping case evidence strictly separate from the reference
    /// corpus.
    /// </summary>
    private static List<IndexableChunk> BuildIndexableChunks(
        Document document,
        IReadOnlyList<DocumentChunk> chunks,
        float[][] embeddings) =>
        chunks
            .Select((chunk, index) => IndexableChunk.FromChunk(
                chunk,
                embeddings[index],
                IndexSourceTypes.CaseDocument,
                document.FileName,
                documentType: document.DocumentType.ToString(),
                caseId: document.CaseId))
            .ToList();

    private static CaseDocumentIndexingResult Result(Document document, int chunkCount) => new()
    {
        DocumentId = document.Id,
        CaseId = document.CaseId,
        ChunkCount = chunkCount,
    };
}
