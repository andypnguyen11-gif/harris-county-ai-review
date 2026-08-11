using System.Text;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.GetDocumentContent;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.Application;

namespace HarrisCountyAI.UnitTests.Documents;

/// <summary>
/// Serving a case document's stored file: scoped to its case, honest about
/// the difference between "no such document" and "the file is gone", and
/// typed so a browser can render it in place.
/// </summary>
public class GetDocumentContentHandlerTests
{
    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-888888888888");
    private static readonly Guid OtherCaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-999999999999");

    private readonly FakeDocumentRepository _documents = new();
    private readonly FakeDocumentStorageService _storage = new();
    private readonly GetDocumentContentHandler _handler;

    public GetDocumentContentHandlerTests()
    {
        _handler = new GetDocumentContentHandler(_documents, _storage);
    }

    private async Task<Document> AddDocumentAsync(
        Guid caseId,
        string fileName = "application.pdf",
        bool storeFile = true)
    {
        var document = Document.Create(caseId, fileName, $"{caseId}/{Guid.NewGuid()}/{fileName}", DocumentType.PermitApplication);
        await _documents.AddAsync(document);

        if (storeFile)
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7 fake"));
            await _storage.UploadAsync(
                DocumentStorageContainer.CaseDocuments, document.BlobPath, "application/pdf", content);
        }

        return document;
    }

    [Fact]
    public async Task A_Stored_File_Is_Returned_With_Its_Name_And_Type()
    {
        var document = await AddDocumentAsync(CaseId);

        var result = await _handler.HandleAsync(CaseId, document.Id);

        Assert.Equal(DocumentContentOutcome.Found, result.Outcome);
        Assert.Equal("application.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);

        using var reader = new StreamReader(result.Content!);
        Assert.Equal("%PDF-1.7 fake", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task An_Unknown_Document_Reports_DocumentNotFound()
    {
        var result = await _handler.HandleAsync(CaseId, Guid.NewGuid());

        Assert.Equal(DocumentContentOutcome.DocumentNotFound, result.Outcome);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Another_Cases_Document_Is_Not_Reachable_By_Id()
    {
        // The lookup is by case id and document id together, so knowing a
        // document id is not enough to read another case's file.
        var document = await AddDocumentAsync(OtherCaseId);

        var result = await _handler.HandleAsync(CaseId, document.Id);

        Assert.Equal(DocumentContentOutcome.DocumentNotFound, result.Outcome);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task A_Record_Without_A_Stored_File_Reports_FileUnavailable()
    {
        var document = await AddDocumentAsync(CaseId, storeFile: false);

        var result = await _handler.HandleAsync(CaseId, document.Id);

        Assert.Equal(DocumentContentOutcome.FileUnavailable, result.Outcome);
        Assert.Null(result.Content);
    }

    [Theory]
    [InlineData("plan.pdf", "application/pdf")]
    [InlineData("PLAN.PDF", "application/pdf")]
    [InlineData("scan.png", "image/png")]
    [InlineData("scan.jpg", "image/jpeg")]
    [InlineData("scan.jpeg", "image/jpeg")]
    [InlineData("scan.tif", "image/tiff")]
    [InlineData("scan.tiff", "image/tiff")]
    [InlineData("notes.txt", DocumentContentTypes.Fallback)]
    [InlineData("no-extension", DocumentContentTypes.Fallback)]
    public void The_Content_Type_Follows_The_File_Extension(string fileName, string expected)
    {
        Assert.Equal(expected, DocumentContentTypes.FromFileName(fileName));
    }

    [Fact]
    public void Constructor_Arguments_Are_Required()
    {
        Assert.Throws<ArgumentNullException>(() => new GetDocumentContentHandler(null!, _storage));
        Assert.Throws<ArgumentNullException>(() => new GetDocumentContentHandler(_documents, null!));
    }
}
