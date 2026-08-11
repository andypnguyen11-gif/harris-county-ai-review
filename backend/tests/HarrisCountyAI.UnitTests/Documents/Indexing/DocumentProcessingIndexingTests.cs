using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.Documents.Extraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.UnitTests.Documents.Indexing;

/// <summary>
/// The processing pipeline hands each normalized document to the case-document
/// indexing service — and stays functional when indexing is unavailable or
/// failing, because the persisted snapshot is what validation depends on.
/// </summary>
public class DocumentProcessingIndexingTests
{
    private readonly FakeDocumentRepository _repository = new();
    private readonly FakeDocumentStorageService _storage = new();
    private readonly FakeDocumentExtractionService _extraction = new();
    private readonly FakeNormalizedDocumentRepository _normalizedRepository = new();
    private readonly FakeCaseDocumentIndexingService _indexing = new();
    private readonly Case _case;

    public DocumentProcessingIndexingTests()
    {
        _case = Case.Create("HC-2026-0002", "Indexing Case", WorkflowType.FloodplainDevelopmentPermit);
    }

    private DocumentProcessingService CreateService(bool withIndexing = true) => new(
        _repository,
        _storage,
        _extraction,
        new DocumentNormalizationService(),
        _normalizedRepository,
        NullLogger<DocumentProcessingService>.Instance,
        withIndexing ? _indexing : null);

    private Document CreateStoredDocument()
    {
        var document = _case.AddDocument("application.pdf", $"cases/{_case.Id}/application.pdf", DocumentType.PermitApplication);
        document.SetProcessingStatus(DocumentProcessingStatus.Uploaded);
        _repository.Add(document);
        _storage.AddBlob(DocumentStorageContainer.CaseDocuments, document.BlobPath, [1, 2, 3]);
        return document;
    }

    [Fact]
    public async Task Processing_Indexes_The_Document_After_Normalization()
    {
        var document = CreateStoredDocument();

        await CreateService().ProcessAsync(document.Id);

        Assert.Equal([document.Id], _indexing.IndexedDocumentIds);
        Assert.Equal(DocumentProcessingStatus.Normalized, document.ProcessingStatus);
    }

    [Fact]
    public async Task An_Indexing_Failure_Does_Not_Fail_Processing()
    {
        var document = CreateStoredDocument();
        _indexing.IndexException = new InvalidOperationException("search service down");

        var normalized = await CreateService().ProcessAsync(document.Id);

        Assert.Equal(document.Id, normalized.DocumentId);
        Assert.Equal(DocumentProcessingStatus.Normalized, document.ProcessingStatus);
        Assert.Single(_normalizedRepository.Added);
    }

    [Fact]
    public async Task Processing_Works_Without_An_Indexing_Service()
    {
        var document = CreateStoredDocument();

        var normalized = await CreateService(withIndexing: false).ProcessAsync(document.Id);

        Assert.Equal(document.Id, normalized.DocumentId);
        Assert.Equal(DocumentProcessingStatus.Normalized, document.ProcessingStatus);
    }

    [Fact]
    public async Task A_Failed_Extraction_Never_Reaches_Indexing()
    {
        var document = CreateStoredDocument();
        _extraction.ExtractException = new InvalidOperationException("Analysis failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().ProcessAsync(document.Id));

        Assert.Empty(_indexing.IndexedDocumentIds);
    }
}
