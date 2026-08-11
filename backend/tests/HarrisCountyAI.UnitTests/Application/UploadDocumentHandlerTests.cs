using System.Text;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.UploadDocument;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Application;

public class UploadDocumentHandlerTests
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");

    private readonly FakeCaseRepository _caseRepository = new();
    private readonly FakeDocumentRepository _documentRepository = new();
    private readonly FakeDocumentStorageService _storage = new();
    private readonly UploadDocumentHandler _handler;

    public UploadDocumentHandlerTests()
    {
        _handler = new UploadDocumentHandler(
            _caseRepository,
            _documentRepository,
            _storage,
            new DocumentFileValidator());
    }

    private async Task<Case> AddCaseAsync()
    {
        var @case = Case.Create("HC-2026-0001", "Upload Target", WorkflowType.FloodplainDevelopmentPermit);
        await _caseRepository.AddAsync(@case);
        return @case;
    }

    private static UploadDocumentCommand Command(
        Guid caseId,
        string fileName = "site-plan.pdf",
        string contentType = "application/pdf",
        long? fileSizeBytes = null,
        DocumentType documentType = DocumentType.SitePlan) =>
        new(caseId, fileName, contentType, fileSizeBytes ?? PdfBytes.Length, new MemoryStream(PdfBytes), documentType);

    [Fact]
    public async Task Uploads_File_And_Persists_Document_With_Uploaded_Status()
    {
        var @case = await AddCaseAsync();

        var result = await _handler.HandleAsync(Command(@case.Id));

        Assert.Equal(UploadDocumentOutcome.Uploaded, result.Outcome);
        var dto = Assert.IsType<DocumentDto>(result.Document);
        Assert.Equal(@case.Id, dto.CaseId);
        Assert.Equal("site-plan.pdf", dto.FileName);
        Assert.Equal(DocumentType.SitePlan, dto.DocumentType);
        Assert.Equal(DocumentProcessingStatus.Uploaded, dto.ProcessingStatus);

        var persisted = Assert.Single(_documentRepository.Documents);
        Assert.Equal(dto.Id, persisted.Id);
        Assert.Equal(DocumentProcessingStatus.Uploaded, persisted.ProcessingStatus);
        Assert.Equal(1, _documentRepository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Uploads_To_CaseDocuments_Container_With_Document_Scoped_Blob_Path()
    {
        var @case = await AddCaseAsync();

        var result = await _handler.HandleAsync(Command(@case.Id));

        var upload = Assert.Single(_storage.Uploads);
        Assert.Equal(DocumentStorageContainer.CaseDocuments, upload.Container);
        Assert.Equal(
            DocumentBlobPathBuilder.ForCaseDocument(@case.Id, result.Document!.Id, "site-plan.pdf"),
            upload.BlobPath);
        Assert.Equal("application/pdf", upload.ContentType);
        Assert.Equal(PdfBytes, upload.Content);

        var persisted = Assert.Single(_documentRepository.Documents);
        Assert.Equal(upload.BlobPath, persisted.BlobPath);
    }

    [Fact]
    public async Task Unknown_Case_Returns_CaseNotFound_Without_Uploading_Or_Persisting()
    {
        var result = await _handler.HandleAsync(Command(Guid.NewGuid()));

        Assert.Equal(UploadDocumentOutcome.CaseNotFound, result.Outcome);
        Assert.Null(result.Document);
        Assert.Empty(_storage.Uploads);
        Assert.Empty(_documentRepository.Documents);
        Assert.Equal(0, _documentRepository.SaveChangesCallCount);
    }

    [Theory]
    [InlineData("malware.exe", "application/pdf")]
    [InlineData("site-plan.pdf", "application/zip")]
    public async Task Invalid_File_Returns_Errors_Without_Uploading_Or_Persisting(string fileName, string contentType)
    {
        var @case = await AddCaseAsync();

        var result = await _handler.HandleAsync(Command(@case.Id, fileName, contentType));

        Assert.Equal(UploadDocumentOutcome.InvalidFile, result.Outcome);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(_storage.Uploads);
        Assert.Empty(_documentRepository.Documents);
    }

    [Fact]
    public async Task Oversized_File_Returns_InvalidFile()
    {
        var @case = await AddCaseAsync();

        var result = await _handler.HandleAsync(
            Command(@case.Id, fileSizeBytes: DocumentFileValidator.DefaultMaxFileSizeBytes + 1));

        Assert.Equal(UploadDocumentOutcome.InvalidFile, result.Outcome);
        Assert.Contains(result.Errors, e => e.Contains("exceeds the maximum allowed size"));
        Assert.Empty(_storage.Uploads);
    }

    [Fact]
    public async Task Storage_Failure_Propagates_And_Nothing_Is_Persisted()
    {
        var @case = await AddCaseAsync();
        _storage.UploadFailure = new InvalidOperationException("storage unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(Command(@case.Id)));

        Assert.Empty(_documentRepository.Documents);
        Assert.Equal(0, _documentRepository.SaveChangesCallCount);
    }
}
