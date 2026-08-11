using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.KnowledgeBase.Ingestion;
using HarrisCountyAI.Application.Search.Chunking;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.Documents.Extraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.UnitTests.KnowledgeBase.Ingestion;

public class KnowledgeDocumentIngestionServiceTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 corpus reference content"u8.ToArray();

    private readonly FakeKnowledgeDocumentRepository _repository = new();
    private readonly FakeDocumentStorageService _storage = new();
    private readonly FakeKnowledgeExtractionService _extraction = new();
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly FakeDocumentIndexService _index = new();

    private KnowledgeDocumentIngestionService CreateService() => new(
        _repository,
        _storage,
        _extraction,
        new StructureAwareChunkingService(),
        _embeddings,
        _index,
        NullLogger<KnowledgeDocumentIngestionService>.Instance);

    private KnowledgeDocument CreateDocument(
        string? version = "2026.1",
        DateOnly? effectiveDate = null,
        string? sourceUrl = "https://www.harriscountytx.gov/permits/regulations.pdf")
    {
        var id = Guid.NewGuid();
        var blobPath = DocumentBlobPathBuilder.ForKnowledgeDocument(id, "floodplain-regulations.pdf");
        var document = KnowledgeDocument.Create(
            id,
            "Floodplain Management Regulations",
            "floodplain-regulations.pdf",
            blobPath,
            "Engineering",
            "Regulation",
            "FloodplainDevelopment",
            version,
            effectiveDate ?? new DateOnly(2026, 1, 15),
            sourceUrl);

        _repository.Add(document);
        _storage.AddBlob(DocumentStorageContainer.KnowledgeBase, blobPath, PdfBytes);
        return document;
    }

    [Fact]
    public async Task IngestAsync_HappyPath_IndexesChunksAndMarksIngested()
    {
        var document = CreateDocument();

        var result = await CreateService().IngestAsync(document.Id);

        Assert.NotNull(result);
        Assert.Equal(IngestionStatus.Succeeded, result.Status);
        Assert.Equal(document.Id, result.DocumentId);
        Assert.True(result.ChunkCount > 0);
        Assert.Null(result.FailureReason);

        Assert.Equal(KnowledgeDocumentIngestionStatus.Ingested, document.IngestionStatus);
        Assert.NotNull(document.IngestionDate);

        var batch = Assert.Single(_index.IndexedBatches);
        Assert.Equal(result.ChunkCount, batch.Count);
    }

    [Fact]
    public async Task IngestAsync_HappyPath_TagsEveryChunkAsKnowledgeBaseWithDocumentMetadata()
    {
        var document = CreateDocument();

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Succeeded, result!.Status);
        var batch = Assert.Single(_index.IndexedBatches);

        Assert.All(batch, chunk =>
        {
            Assert.Equal(IndexSourceTypes.KnowledgeBase, chunk.SourceType);
            Assert.Null(chunk.CaseId);
            Assert.Equal(document.Id, chunk.DocumentId);
            Assert.Equal("Floodplain Management Regulations", chunk.Title);
            Assert.Equal("Engineering", chunk.Department);
            Assert.Equal("FloodplainDevelopment", chunk.PermitType);
            Assert.Equal("Regulation", chunk.DocumentType);
            Assert.Equal("https://www.harriscountytx.gov/permits/regulations.pdf", chunk.SourceUrl);
            Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), chunk.EffectiveDate);
        });

        // Chunks arrive in document order with the expected key format.
        for (var i = 0; i < batch.Count; i++)
        {
            Assert.Equal(i, batch[i].Sequence);
            Assert.Equal($"{document.Id:N}-{i:D4}", batch[i].ChunkId);
        }
    }

    [Fact]
    public async Task IngestAsync_HappyPath_EnsuresIndexThenDeletesThenIndexes()
    {
        var document = CreateDocument();

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Succeeded, result!.Status);
        Assert.Equal(
            ["EnsureIndex", $"Delete:{document.Id}", $"Index:{result.ChunkCount}"],
            _index.Operations);
    }

    [Fact]
    public async Task IngestAsync_HappyPath_TransitionsThroughProcessing()
    {
        var document = CreateDocument();

        await CreateService().IngestAsync(document.Id);

        Assert.Equal(
            [KnowledgeDocumentIngestionStatus.Processing, KnowledgeDocumentIngestionStatus.Ingested],
            _repository.SavedStatuses);
    }

    [Fact]
    public async Task IngestAsync_MatchesEmbeddingsToChunksByInputIndex()
    {
        var document = CreateDocument();
        _embeddings.ReverseResultOrder = true;

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Succeeded, result!.Status);
        var batch = Assert.Single(_index.IndexedBatches);

        // The fake sets vector[0] to inputIndex + 1; even with results
        // returned in reverse order, chunk i must carry embedding i.
        for (var i = 0; i < batch.Count; i++)
        {
            Assert.Equal(i + 1, batch[i].Embedding[0]);
        }
    }

    [Fact]
    public async Task IngestAsync_UnknownDocument_ReturnsNull()
    {
        var result = await CreateService().IngestAsync(Guid.NewGuid());

        Assert.Null(result);
        Assert.Empty(_index.Operations);
        Assert.Empty(_repository.SavedStatuses);
    }

    [Fact]
    public async Task IngestAsync_ExtractionFailure_MarksFailedAndIndexesNothing()
    {
        var document = CreateDocument();
        _extraction.ExtractException = new InvalidOperationException("Document Intelligence is unavailable.");

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Failed, result!.Status);
        Assert.Equal(0, result.ChunkCount);
        Assert.Contains("Document Intelligence is unavailable.", result.FailureReason);

        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Null(document.IngestionDate);
        Assert.Empty(_index.Operations);
        Assert.Empty(_embeddings.Requests);
    }

    [Fact]
    public async Task IngestAsync_EmbeddingFailure_MarksFailedAndIndexesNothing()
    {
        var document = CreateDocument();
        _embeddings.EmbedException = new InvalidOperationException("The embedding deployment rejected the request.");

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Failed, result!.Status);
        Assert.Contains("embedding deployment rejected", result.FailureReason);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Empty(_index.Operations);
    }

    [Fact]
    public async Task IngestAsync_EmbeddingCountMismatch_MarksFailed()
    {
        var document = CreateDocument();
        _embeddings.ResultCountOverride = 0;

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Failed, result!.Status);
        Assert.Contains("embedding service returned", result.FailureReason);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Empty(_index.Operations);
    }

    [Fact]
    public async Task IngestAsync_IndexingFailure_MarksFailed()
    {
        var document = CreateDocument();
        _index.IndexException = new InvalidOperationException("The search service returned 503.");

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Failed, result!.Status);
        Assert.Contains("503", result.FailureReason);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Empty(_index.IndexedBatches);
    }

    [Fact]
    public async Task IngestAsync_MissingBlob_MarksFailed()
    {
        var document = CreateDocument();
        _storage.DownloadException = new FileNotFoundException("Blob was not found.");

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Failed, result!.Status);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
    }

    [Fact]
    public async Task IngestAsync_DocumentWithNoText_MarksFailedWithoutEmbedding()
    {
        var document = CreateDocument();
        _extraction.Pages = [];
        _extraction.RawText = "   ";

        var result = await CreateService().IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Failed, result!.Status);
        Assert.Contains("no extractable text", result.FailureReason);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Empty(_embeddings.Requests);
        Assert.Empty(_index.Operations);
    }

    [Fact]
    public async Task IngestAsync_FailedDocument_CanBeReprocessedToIngested()
    {
        var document = CreateDocument();
        var service = CreateService();

        _extraction.ExtractException = new InvalidOperationException("Transient outage.");
        var failed = await service.IngestAsync(document.Id);
        Assert.Equal(IngestionStatus.Failed, failed!.Status);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);

        _extraction.ExtractException = null;
        var succeeded = await service.IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Succeeded, succeeded!.Status);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Ingested, document.IngestionStatus);
    }

    [Fact]
    public async Task IngestAsync_IngestedDocument_ReindexesWithDeleteBeforeIndex()
    {
        var document = CreateDocument();
        var service = CreateService();

        var first = await service.IngestAsync(document.Id);
        var second = await service.IngestAsync(document.Id);

        Assert.Equal(IngestionStatus.Succeeded, first!.Status);
        Assert.Equal(IngestionStatus.Succeeded, second!.Status);
        Assert.Equal(KnowledgeDocumentIngestionStatus.Ingested, document.IngestionStatus);

        // Each run deletes the document's chunks before indexing the new batch.
        Assert.Equal(
            [
                "EnsureIndex", $"Delete:{document.Id}", $"Index:{first.ChunkCount}",
                "EnsureIndex", $"Delete:{document.Id}", $"Index:{second.ChunkCount}",
            ],
            _index.Operations);
    }

    [Fact]
    public async Task IngestAsync_DeactivatedDocument_ThrowsAndStaysDeactivated()
    {
        var document = CreateDocument();
        document.Deactivate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().IngestAsync(document.Id));

        Assert.Equal(KnowledgeDocumentIngestionStatus.Deactivated, document.IngestionStatus);
        Assert.Empty(_index.Operations);
    }

    [Fact]
    public async Task IngestAsync_Cancellation_MarksFailedAndRethrows()
    {
        var document = CreateDocument();
        _extraction.ExtractException = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService().IngestAsync(document.Id));

        Assert.Equal(KnowledgeDocumentIngestionStatus.Failed, document.IngestionStatus);
        Assert.Empty(_index.Operations);
    }
}
