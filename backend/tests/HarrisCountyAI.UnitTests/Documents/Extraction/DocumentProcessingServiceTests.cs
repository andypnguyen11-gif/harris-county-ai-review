using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.UnitTests.Documents.Extraction;

public class DocumentProcessingServiceTests
{
    private readonly FakeDocumentRepository _repository = new();
    private readonly FakeDocumentStorageService _storage = new();
    private readonly FakeDocumentExtractionService _extraction = new();
    private readonly FakeNormalizedDocumentRepository _normalizedRepository = new();
    private readonly DocumentProcessingService _service;
    private readonly Case _case;

    public DocumentProcessingServiceTests()
    {
        _service = new DocumentProcessingService(
            _repository,
            _storage,
            _extraction,
            new DocumentNormalizationService(),
            _normalizedRepository,
            NullLogger<DocumentProcessingService>.Instance);

        _case = Case.Create("HC-2026-0001", "Processing Case", WorkflowType.FloodplainDevelopmentPermit);
    }

    private Document CreateStoredDocument()
    {
        var document = _case.AddDocument("application.pdf", $"cases/{_case.Id}/application.pdf", DocumentType.PermitApplication);
        document.SetProcessingStatus(DocumentProcessingStatus.Uploaded);
        _repository.Add(document);
        _storage.AddBlob(DocumentStorageContainer.CaseDocuments, document.BlobPath, [1, 2, 3]);
        return document;
    }

    [Fact]
    public async Task Successful_Processing_Transitions_Through_Extracting_And_Extracted_To_Normalized()
    {
        var document = CreateStoredDocument();

        var normalized = await _service.ProcessAsync(document.Id);

        Assert.Equal(document.Id, normalized.DocumentId);
        Assert.Equal(DocumentProcessingStatus.Normalized, document.ProcessingStatus);
        Assert.Equal(
            [DocumentProcessingStatus.Extracting, DocumentProcessingStatus.Extracted, DocumentProcessingStatus.Normalized],
            _repository.SavedStatuses);
    }

    [Fact]
    public async Task Persists_The_Normalized_Document_With_Case_And_Type()
    {
        var document = CreateStoredDocument();

        var normalized = await _service.ProcessAsync(document.Id);

        var persisted = Assert.Single(_normalizedRepository.Added);
        Assert.Same(normalized, persisted);
        Assert.Equal(_case.Id, persisted.CaseId);
        Assert.Equal(DocumentType.PermitApplication, persisted.DocumentType);
        Assert.Equal("Fake page text", persisted.RawText);
        Assert.Single(persisted.Pages);
        Assert.Contains(persisted.Fields, field => field.Name == "applicant name" && field.Value == "Jane Doe");
        Assert.Equal(1, _normalizedRepository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Downloads_The_Document_Blob_And_Extracts_It()
    {
        var document = CreateStoredDocument();

        await _service.ProcessAsync(document.Id);

        Assert.Equal([document.BlobPath], _storage.DownloadedPaths);
        Assert.Equal(document.Id, _extraction.LastDocumentId);
    }

    [Fact]
    public async Task Extraction_Failure_Marks_Document_Failed_And_Rethrows()
    {
        var document = CreateStoredDocument();
        _extraction.ExtractException = new InvalidOperationException("Analysis failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ProcessAsync(document.Id));

        Assert.Equal(DocumentProcessingStatus.Failed, document.ProcessingStatus);
        Assert.Equal(
            [DocumentProcessingStatus.Extracting, DocumentProcessingStatus.Failed],
            _repository.SavedStatuses);
        Assert.Empty(_normalizedRepository.Added);
    }

    [Fact]
    public async Task Download_Failure_Marks_Document_Failed_And_Rethrows()
    {
        var document = CreateStoredDocument();
        _storage.DownloadException = new FileNotFoundException("Blob missing.");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _service.ProcessAsync(document.Id));

        Assert.Equal(DocumentProcessingStatus.Failed, document.ProcessingStatus);
        Assert.Empty(_normalizedRepository.Added);
    }

    [Fact]
    public async Task Unknown_Document_Throws_Without_Persisting_Anything()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ProcessAsync(Guid.NewGuid()));

        Assert.Equal(0, _repository.SaveChangesCallCount);
        Assert.Empty(_normalizedRepository.Added);
    }
}
