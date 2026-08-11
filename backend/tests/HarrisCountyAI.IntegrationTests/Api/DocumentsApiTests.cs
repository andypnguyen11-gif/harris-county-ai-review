using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.Api;

public class DocumentsApiTests : IClassFixture<SqlServerTestDatabase>, IAsyncLifetime
{
    /// <summary>A minimal but structurally real single-page PDF.</summary>
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\n" +
        "trailer<</Root 1 0 R>>\n" +
        "%%EOF\n");

    private readonly SqlServerTestDatabase _database;
    private readonly string _containerName;
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<TestApplicationFactory> _factories = [];

    public DocumentsApiTests(SqlServerTestDatabase database)
    {
        _database = database;
        _containerName = $"test-docs-api-{Guid.NewGuid():N}"[..40];
        _factory = CreateFactory();
        _client = _factory.CreateClient().WithToken(TestAuthentication.CreateToken(roles: ["Reviewer"]));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }

        var blobClient = BlobStorageServiceExtensions.CreateBlobServiceClient("UseDevelopmentStorage=true");
        await blobClient.GetBlobContainerClient(_containerName).DeleteIfExistsAsync();
    }

    private TestApplicationFactory CreateFactory(long? maxFileSizeBytes = null)
    {
        var factory = new TestApplicationFactory { ConnectionStringOverride = _database.ConnectionString };
        factory.BlobStorageOverrides["CaseDocumentsContainerName"] = _containerName;
        if (maxFileSizeBytes is not null)
        {
            factory.BlobStorageOverrides["MaxFileSizeBytes"] = maxFileSizeBytes.Value.ToString();
        }

        _factories.Add(factory);
        return factory;
    }

    private static MultipartFormDataContent BuildUpload(
        byte[] content,
        string fileName = "site-plan.pdf",
        string contentType = "application/pdf",
        string? documentType = "SitePlan")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (documentType is not null)
        {
            form.Add(new StringContent(documentType), "documentType");
        }

        return form;
    }

    private async Task<Guid> CreateCaseAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/cases", new
        {
            name = "Document Upload Case",
            workflowType = "FloodplainDevelopmentPermit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> UploadAsync(Guid caseId, string fileName = "site-plan.pdf", string documentType = "SitePlan")
    {
        var response = await _client.PostAsync(
            $"/api/cases/{caseId}/documents",
            BuildUpload(PdfBytes, fileName, documentType: documentType));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    [Fact]
    public async Task Post_Uploads_Pdf_And_Returns_201_With_Document()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.PostAsync($"/api/cases/{caseId}/documents", BuildUpload(PdfBytes));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var documentId = root.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, documentId);
        Assert.Equal(caseId, root.GetProperty("caseId").GetGuid());
        Assert.Equal("site-plan.pdf", root.GetProperty("fileName").GetString());
        Assert.Equal("SitePlan", root.GetProperty("documentType").GetString());
        Assert.Equal("Uploaded", root.GetProperty("processingStatus").GetString());
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("createdAt").GetString(), out _));

        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith(
            $"/api/cases/{caseId}/documents/{documentId}",
            response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_Stores_Blob_In_Azurite_At_Document_Scoped_Path()
    {
        var caseId = await CreateCaseAsync();

        var document = await UploadAsync(caseId, "Site Plan Rev 2.pdf");
        var documentId = document.GetProperty("id").GetGuid();

        var expectedPath = DocumentBlobPathBuilder.ForCaseDocument(caseId, documentId, "Site Plan Rev 2.pdf");
        var blob = BlobStorageServiceExtensions.CreateBlobServiceClient("UseDevelopmentStorage=true")
            .GetBlobContainerClient(_containerName)
            .GetBlobClient(expectedPath);

        Assert.True(await blob.ExistsAsync(), $"Expected blob at '{expectedPath}'.");

        var downloaded = await blob.DownloadContentAsync();
        Assert.Equal(PdfBytes, downloaded.Value.Content.ToArray());
        Assert.Equal("application/pdf", downloaded.Value.Details.ContentType);
    }

    [Fact]
    public async Task Post_To_Unknown_Case_Returns_404_ProblemDetails()
    {
        var response = await _client.PostAsync($"/api/cases/{Guid.NewGuid()}/documents", BuildUpload(PdfBytes));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_With_Disallowed_Extension_Returns_400_ProblemDetails()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.PostAsync(
            $"/api/cases/{caseId}/documents",
            BuildUpload(PdfBytes, "notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        Assert.True(body.RootElement.TryGetProperty("errors", out var errorsElement), $"body: {raw}");
        Assert.True(errorsElement.TryGetProperty("file", out var fileErrors), $"body: {raw}");
        var errors = fileErrors.EnumerateArray().ToList();
        Assert.Contains(errors, e => e.GetString()!.Contains(".docx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_With_Oversized_File_Returns_400_ProblemDetails()
    {
        var factory = CreateFactory(maxFileSizeBytes: PdfBytes.Length - 1);
        using var client = factory.CreateClient().WithToken(TestAuthentication.CreateToken(roles: ["Reviewer"]));

        var createCase = await client.PostAsJsonAsync("/api/cases", new
        {
            name = "Oversize Case",
            workflowType = "FloodplainDevelopmentPermit",
        });
        var caseId = (await createCase.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/cases/{caseId}/documents", BuildUpload(PdfBytes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors").GetProperty("file").EnumerateArray().ToList();
        Assert.Contains(errors, e => e.GetString()!.Contains("exceeds the maximum allowed size"));
    }

    [Fact]
    public async Task Post_With_Invalid_DocumentType_Returns_400_ProblemDetails()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.PostAsync(
            $"/api/cases/{caseId}/documents",
            BuildUpload(PdfBytes, documentType: "NotARealType"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_Without_File_Returns_400_ProblemDetails()
    {
        var caseId = await CreateCaseAsync();

        using var form = new MultipartFormDataContent { { new StringContent("SitePlan"), "documentType" } };
        var response = await _client.PostAsync($"/api/cases/{caseId}/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_List_Returns_Empty_Array_For_Case_Without_Documents()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.GetAsync($"/api/cases/{caseId}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Empty(body.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Get_List_Returns_Uploaded_Documents()
    {
        var caseId = await CreateCaseAsync();
        var first = await UploadAsync(caseId, "application.pdf", "PermitApplication");
        var second = await UploadAsync(caseId, "elevation.pdf", "ElevationCertificate");

        var response = await _client.GetAsync($"/api/cases/{caseId}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documents = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, documents.Count);

        var ids = documents.Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(first.GetProperty("id").GetGuid(), ids);
        Assert.Contains(second.GetProperty("id").GetGuid(), ids);
        Assert.All(documents, d => Assert.Equal("Uploaded", d.GetProperty("processingStatus").GetString()));
    }

    [Fact]
    public async Task Get_List_For_Unknown_Case_Returns_404_ProblemDetails()
    {
        var response = await _client.GetAsync($"/api/cases/{Guid.NewGuid()}/documents");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_ById_Returns_Document()
    {
        var caseId = await CreateCaseAsync();
        var uploaded = await UploadAsync(caseId, "affidavit.pdf", "Affidavit");
        var documentId = uploaded.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/cases/{caseId}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(documentId, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("affidavit.pdf", body.RootElement.GetProperty("fileName").GetString());
        Assert.Equal("Affidavit", body.RootElement.GetProperty("documentType").GetString());
    }

    [Fact]
    public async Task Get_ById_Returns_404_For_Unknown_Document()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.GetAsync($"/api/cases/{caseId}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ById_Returns_404_When_Document_Belongs_To_Another_Case()
    {
        var firstCaseId = await CreateCaseAsync();
        var secondCaseId = await CreateCaseAsync();
        var uploaded = await UploadAsync(firstCaseId);
        var documentId = uploaded.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/cases/{secondCaseId}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Content_Streams_The_Stored_File_For_Inline_Viewing()
    {
        var caseId = await CreateCaseAsync();
        var uploaded = await UploadAsync(caseId, "site-plan.pdf");
        var documentId = uploaded.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/cases/{caseId}/documents/{documentId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfBytes, await response.Content.ReadAsByteArrayAsync());

        // Inline, so the viewer renders the file rather than downloading it.
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("inline", disposition?.DispositionType);
        Assert.Contains("site-plan.pdf", disposition?.ToString());
    }

    [Fact]
    public async Task Get_Content_Returns_404_For_An_Unknown_Document()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.GetAsync($"/api/cases/{caseId}/documents/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_Content_Does_Not_Serve_Another_Cases_Document()
    {
        var firstCaseId = await CreateCaseAsync();
        var secondCaseId = await CreateCaseAsync();
        var uploaded = await UploadAsync(firstCaseId);
        var documentId = uploaded.GetProperty("id").GetGuid();

        var response = await _client.GetAsync(
            $"/api/cases/{secondCaseId}/documents/{documentId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
