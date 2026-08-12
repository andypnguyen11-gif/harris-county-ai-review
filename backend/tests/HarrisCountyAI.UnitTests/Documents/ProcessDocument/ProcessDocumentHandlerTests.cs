using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Application.Documents.ProcessDocument;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.Documents.Extraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.UnitTests.Documents.ProcessDocument;

/// <summary>
/// Covers the case-scoped trigger for the extraction pipeline. The real
/// <see cref="DocumentProcessingService"/> runs underneath the handler, with
/// only storage and extraction faked, so the status transitions asserted here
/// are the ones the pipeline actually persists.
/// </summary>
public class ProcessDocumentHandlerTests
{
    private readonly FakeDocumentRepository _repository = new();
    private readonly FakeDocumentStorageService _storage = new();
    private readonly FakeDocumentExtractionService _extraction = new();
    private readonly FakeNormalizedDocumentRepository _normalizedRepository = new();
    private readonly ProcessDocumentHandler _handler;
    private readonly Case _case;

    public ProcessDocumentHandlerTests()
    {
        _case = Case.Create("HC-2026-0100", "Processing Trigger Case", WorkflowType.FloodplainDevelopmentPermit);

        _handler = new ProcessDocumentHandler(
            _repository,
            new DocumentProcessingService(
                _repository,
                _storage,
                _extraction,
                new DocumentNormalizationService(),
                _normalizedRepository,
                NullLogger<DocumentProcessingService>.Instance),
            NullLogger<ProcessDocumentHandler>.Instance);
    }

    private Document CreateUploadedDocument(Case? owningCase = null)
    {
        var target = owningCase ?? _case;
        var document = target.AddDocument(
            "application.pdf", $"cases/{target.Id}/application.pdf", DocumentType.PermitApplication);
        document.SetProcessingStatus(DocumentProcessingStatus.Uploaded);
        _repository.Add(document);
        _storage.AddBlob(DocumentStorageContainer.CaseDocuments, document.BlobPath, [1, 2, 3]);
        return document;
    }

    [Fact]
    public async Task Processing_An_Uploaded_Document_Reports_It_Normalized_Without_A_Failure_Reason()
    {
        var document = CreateUploadedDocument();

        var result = await _handler.HandleAsync(_case.Id, document.Id);

        Assert.NotNull(result);
        Assert.Null(result.FailureReason);
        Assert.Equal(document.Id, result.Document.Id);
        Assert.Equal(_case.Id, result.Document.CaseId);
        Assert.Equal(DocumentProcessingStatus.Normalized, result.Document.ProcessingStatus);
        Assert.Equal(DocumentProcessingStatus.Normalized, document.ProcessingStatus);
    }

    [Fact]
    public async Task Processing_Persists_The_Normalized_Snapshot_Validation_Reads()
    {
        var document = CreateUploadedDocument();

        await _handler.HandleAsync(_case.Id, document.Id);

        var normalized = Assert.Single(_normalizedRepository.Added);
        Assert.Equal(document.Id, normalized.DocumentId);
        Assert.Equal(_case.Id, normalized.CaseId);
    }

    [Fact]
    public async Task Processing_Moves_The_Document_Through_Extracting_And_Extracted_To_Normalized()
    {
        var document = CreateUploadedDocument();

        await _handler.HandleAsync(_case.Id, document.Id);

        Assert.Equal(
            [
                DocumentProcessingStatus.Extracting,
                DocumentProcessingStatus.Extracted,
                DocumentProcessingStatus.Normalized,
            ],
            _repository.SavedStatuses);
    }

    [Fact]
    public async Task A_Pipeline_Failure_Is_Reported_With_Its_Reason_Instead_Of_Being_Rethrown()
    {
        var document = CreateUploadedDocument();
        _extraction.ExtractException = new InvalidOperationException(
            "The file could not be analyzed: unexpected end of stream.");

        var result = await _handler.HandleAsync(_case.Id, document.Id);

        Assert.NotNull(result);
        Assert.Contains("could not be analyzed", result.FailureReason);
    }

    [Fact]
    public async Task A_Pipeline_Failure_Leaves_The_Document_In_The_Terminal_Failed_Status()
    {
        var document = CreateUploadedDocument();
        _extraction.ExtractException = new InvalidOperationException("Extraction outage.");

        var result = await _handler.HandleAsync(_case.Id, document.Id);

        // Reported and persisted, so the document is never left sitting at
        // Uploaded with nothing to show the reviewer.
        Assert.Equal(DocumentProcessingStatus.Failed, result!.Document.ProcessingStatus);
        Assert.Equal(DocumentProcessingStatus.Failed, document.ProcessingStatus);
        Assert.Equal(DocumentProcessingStatus.Failed, _repository.SavedStatuses[^1]);
        Assert.Empty(_normalizedRepository.Added);
    }

    [Fact]
    public async Task A_Failed_Document_Can_Be_Reprocessed_Once_The_Underlying_Problem_Clears()
    {
        var document = CreateUploadedDocument();
        _extraction.ExtractException = new InvalidOperationException("Extraction outage.");
        Assert.Equal(DocumentProcessingStatus.Failed, (await _handler.HandleAsync(_case.Id, document.Id))!.Document.ProcessingStatus);

        _extraction.ExtractException = null;
        var retry = await _handler.HandleAsync(_case.Id, document.Id);

        Assert.Null(retry!.FailureReason);
        Assert.Equal(DocumentProcessingStatus.Normalized, retry.Document.ProcessingStatus);
    }

    [Fact]
    public async Task An_Unknown_Document_Is_Not_Found_And_Never_Reaches_The_Pipeline()
    {
        var result = await _handler.HandleAsync(_case.Id, Guid.NewGuid());

        Assert.Null(result);
        Assert.Null(_extraction.LastDocumentId);
    }

    [Fact]
    public async Task A_Document_Belonging_To_Another_Case_Is_Not_Found_And_Never_Reaches_The_Pipeline()
    {
        var otherCase = Case.Create("HC-2026-0101", "Other Case", WorkflowType.FloodplainDevelopmentPermit);
        var document = CreateUploadedDocument(otherCase);

        var result = await _handler.HandleAsync(_case.Id, document.Id);

        Assert.Null(result);
        Assert.Null(_extraction.LastDocumentId);
        Assert.Equal(DocumentProcessingStatus.Uploaded, document.ProcessingStatus);
    }
}
