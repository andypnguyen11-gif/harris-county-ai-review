using System.Security.Cryptography;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;

namespace HarrisCountyAI.IntegrationTests.BlobStorage;

public class AzureBlobDocumentStorageServiceTests : IClassFixture<AzuriteBlobStorageFixture>
{
    private readonly AzuriteBlobStorageFixture _fixture;

    public AzureBlobDocumentStorageServiceTests(AzuriteBlobStorageFixture fixture)
    {
        _fixture = fixture;
    }

    private static byte[] RandomContent(int length) => RandomNumberGenerator.GetBytes(length);

    private static string NewCaseDocumentPath(string fileName = "permit.pdf")
        => DocumentBlobPathBuilder.ForCaseDocument(Guid.NewGuid(), Guid.NewGuid(), fileName);

    [Fact]
    public async Task Upload_Exists_Download_Delete_Roundtrip()
    {
        var blobPath = NewCaseDocumentPath();
        var content = RandomContent(1024);

        using (var upload = new MemoryStream(content))
        {
            var storedPath = await _fixture.Service.UploadAsync(
                DocumentStorageContainer.CaseDocuments, blobPath, "application/pdf", upload);
            Assert.Equal(blobPath, storedPath);
        }

        Assert.True(await _fixture.Service.ExistsAsync(DocumentStorageContainer.CaseDocuments, blobPath));

        await using (var download = await _fixture.Service.DownloadAsync(
            DocumentStorageContainer.CaseDocuments, blobPath))
        {
            using var downloaded = new MemoryStream();
            await download.CopyToAsync(downloaded);
            Assert.Equal(content, downloaded.ToArray());
        }

        Assert.True(await _fixture.Service.DeleteAsync(DocumentStorageContainer.CaseDocuments, blobPath));
        Assert.False(await _fixture.Service.ExistsAsync(DocumentStorageContainer.CaseDocuments, blobPath));
    }

    [Fact]
    public async Task Upload_Stores_The_Content_Type()
    {
        var blobPath = NewCaseDocumentPath("scan.png");
        using var upload = new MemoryStream(RandomContent(64));

        await _fixture.Service.UploadAsync(
            DocumentStorageContainer.CaseDocuments, blobPath, "image/png", upload);

        var blobClient = _fixture.Client
            .GetBlobContainerClient(_fixture.Options.CaseDocumentsContainerName)
            .GetBlobClient(blobPath);
        var properties = await blobClient.GetPropertiesAsync();
        Assert.Equal("image/png", properties.Value.ContentType);

        await _fixture.Service.DeleteAsync(DocumentStorageContainer.CaseDocuments, blobPath);
    }

    [Fact]
    public async Task Upload_Overwrites_Existing_Blob_At_Same_Path()
    {
        var blobPath = NewCaseDocumentPath();
        var replacement = RandomContent(256);

        using (var first = new MemoryStream(RandomContent(128)))
        {
            await _fixture.Service.UploadAsync(
                DocumentStorageContainer.CaseDocuments, blobPath, "application/pdf", first);
        }

        using (var second = new MemoryStream(replacement))
        {
            await _fixture.Service.UploadAsync(
                DocumentStorageContainer.CaseDocuments, blobPath, "application/pdf", second);
        }

        await using var download = await _fixture.Service.DownloadAsync(
            DocumentStorageContainer.CaseDocuments, blobPath);
        using var downloaded = new MemoryStream();
        await download.CopyToAsync(downloaded);
        Assert.Equal(replacement, downloaded.ToArray());

        await _fixture.Service.DeleteAsync(DocumentStorageContainer.CaseDocuments, blobPath);
    }

    [Fact]
    public async Task Exists_Returns_False_For_Missing_Blob()
    {
        Assert.False(await _fixture.Service.ExistsAsync(
            DocumentStorageContainer.CaseDocuments, NewCaseDocumentPath("missing.pdf")));
    }

    [Fact]
    public async Task Delete_Returns_False_For_Missing_Blob()
    {
        Assert.False(await _fixture.Service.DeleteAsync(
            DocumentStorageContainer.CaseDocuments, NewCaseDocumentPath("missing.pdf")));
    }

    [Fact]
    public async Task Download_Throws_FileNotFound_For_Missing_Blob()
    {
        var blobPath = NewCaseDocumentPath("missing.pdf");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _fixture.Service.DownloadAsync(DocumentStorageContainer.CaseDocuments, blobPath));

        Assert.Contains(blobPath, exception.Message);
    }

    [Fact]
    public async Task Containers_Are_Isolated_Between_CaseDocuments_And_KnowledgeBase()
    {
        var blobPath = "shared/requirements.pdf";
        using var upload = new MemoryStream(RandomContent(64));

        await _fixture.Service.UploadAsync(
            DocumentStorageContainer.KnowledgeBase, blobPath, "application/pdf", upload);

        Assert.True(await _fixture.Service.ExistsAsync(DocumentStorageContainer.KnowledgeBase, blobPath));
        Assert.False(await _fixture.Service.ExistsAsync(DocumentStorageContainer.CaseDocuments, blobPath));

        await _fixture.Service.DeleteAsync(DocumentStorageContainer.KnowledgeBase, blobPath);
    }

    [Fact]
    public async Task Upload_Creates_The_Container_When_It_Does_Not_Exist()
    {
        var options = new BlobStorageOptions
        {
            ConnectionString = AzuriteBlobStorageFixture.ConnectionString,
            CaseDocumentsContainerName = $"test-created-on-demand-{Guid.NewGuid():N}"[..48],
            KnowledgeBaseContainerName = $"test-created-on-demand-kb-{Guid.NewGuid():N}"[..48],
        };
        var service = new AzureBlobDocumentStorageService(
            _fixture.Client, Microsoft.Extensions.Options.Options.Create(options));
        var containerClient = _fixture.Client.GetBlobContainerClient(options.CaseDocumentsContainerName);

        try
        {
            Assert.False(await containerClient.ExistsAsync());

            using var upload = new MemoryStream(RandomContent(16));
            await service.UploadAsync(
                DocumentStorageContainer.CaseDocuments, NewCaseDocumentPath(), "application/pdf", upload);

            Assert.True(await containerClient.ExistsAsync());
        }
        finally
        {
            await containerClient.DeleteIfExistsAsync();
        }
    }

    [Fact]
    public void Validator_Rejects_Oversized_File_Before_It_Reaches_Storage()
    {
        var validator = new DocumentFileValidator(_fixture.Options.MaxFileSizeBytes);

        var result = validator.Validate("huge-scan.pdf", "application/pdf", _fixture.Options.MaxFileSizeBytes + 1);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("exceeds the maximum", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_Rejects_Disallowed_File_Type_Before_It_Reaches_Storage()
    {
        var validator = new DocumentFileValidator(_fixture.Options.MaxFileSizeBytes);

        var result = validator.Validate("script.exe", "application/octet-stream", 1024);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }
}
