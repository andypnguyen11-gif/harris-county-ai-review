using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HarrisCountyAI.Application.KnowledgeBase;
using HarrisCountyAI.Application.KnowledgeBase.DeactivateKnowledgeDocument;
using HarrisCountyAI.Application.KnowledgeBase.GetKnowledgeDocuments;
using HarrisCountyAI.Application.KnowledgeBase.UploadKnowledgeDocument;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using HarrisCountyAI.Infrastructure.Persistence.Repositories;
using HarrisCountyAI.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests.Api;

public class KnowledgeBaseApiTests : IClassFixture<SqlServerTestDatabase>, IAsyncLifetime
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 knowledge base test content"u8.ToArray();

    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _knowledgeBaseContainerName = $"test-kb-api-{Guid.NewGuid():N}"[..36];

    public KnowledgeBaseApiTests(SqlServerTestDatabase database)
    {
        _factory = new TestApplicationFactory
        {
            ConnectionStringOverride = database.ConnectionString,
            // Knowledge-base services are not yet wired into the production
            // composition root; register them here until that lands.
            TestServices = services =>
            {
                services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
                services.AddScoped<UploadKnowledgeDocumentHandler>();
                services.AddScoped<GetKnowledgeDocumentsHandler>();
                services.AddScoped<DeactivateKnowledgeDocumentHandler>();
            },
        };
        _factory.SettingOverrides["BlobStorage:KnowledgeBaseContainerName"] = _knowledgeBaseContainerName;
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        var client = BlobStorageServiceExtensions.CreateBlobServiceClient("UseDevelopmentStorage=true");
        await client.GetBlobContainerClient(_knowledgeBaseContainerName).DeleteIfExistsAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Post_Uploads_Document_And_Returns_201_With_Metadata()
    {
        var response = await PostDocumentAsync(title: "Floodplain Development Permit Application");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var id = root.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.EndsWith(
            $"/api/knowledge-base/documents/{id}",
            response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Floodplain Development Permit Application", root.GetProperty("title").GetString());
        Assert.Equal("permit-application.pdf", root.GetProperty("fileName").GetString());
        Assert.Equal($"knowledge/{id:D}_permit-application.pdf", root.GetProperty("blobPath").GetString());
        Assert.Equal("Engineering", root.GetProperty("department").GetString());
        Assert.Equal("PermitForm", root.GetProperty("documentType").GetString());
        Assert.Equal("FloodplainDevelopment", root.GetProperty("permitType").GetString());
        Assert.Equal("2026.1", root.GetProperty("version").GetString());
        Assert.Equal("2026-01-15", root.GetProperty("effectiveDate").GetString());
        Assert.Equal("https://www.harriscountytx.gov/permits/form.pdf", root.GetProperty("sourceUrl").GetString());
        Assert.Equal("Uploaded", root.GetProperty("ingestionStatus").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ingestionDate").ValueKind);
    }

    [Fact]
    public async Task Post_Stores_File_Content_In_Blob_Storage()
    {
        var response = await PostDocumentAsync(title: "Blob Roundtrip");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var blobPath = body.RootElement.GetProperty("blobPath").GetString()!;

        var client = BlobStorageServiceExtensions.CreateBlobServiceClient("UseDevelopmentStorage=true");
        var blobClient = client.GetBlobContainerClient(_knowledgeBaseContainerName).GetBlobClient(blobPath);

        Assert.True(await blobClient.ExistsAsync());

        var downloaded = await blobClient.DownloadContentAsync();
        Assert.Equal(PdfBytes, downloaded.Value.Content.ToArray());
        Assert.Equal("application/pdf", downloaded.Value.Details.ContentType);
    }

    [Fact]
    public async Task Post_Persists_Metadata_Visible_In_List()
    {
        var response = await PostDocumentAsync(title: "Listed Knowledge Document");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();

        var listed = await GetDocumentsAsync();
        var document = listed.Single(d => d.GetProperty("id").GetGuid() == id);

        Assert.Equal("Listed Knowledge Document", document.GetProperty("title").GetString());
        Assert.Equal("Engineering", document.GetProperty("department").GetString());
        Assert.Equal("PermitForm", document.GetProperty("documentType").GetString());
        Assert.Equal("FloodplainDevelopment", document.GetProperty("permitType").GetString());
        Assert.Equal("2026-01-15", document.GetProperty("effectiveDate").GetString());
        Assert.Equal("Uploaded", document.GetProperty("ingestionStatus").GetString());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("department")]
    [InlineData("documentType")]
    [InlineData("permitType")]
    public async Task Post_Without_Required_Metadata_Returns_400_ProblemDetails(string missingField)
    {
        var response = await PostDocumentAsync(omitField: missingField);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(missingField, out _));
    }

    [Fact]
    public async Task Post_Without_File_Returns_400()
    {
        using var form = BuildForm(includeFile: false);
        var response = await _client.PostAsync("/api/knowledge-base/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_With_Disallowed_File_Type_Returns_400()
    {
        var response = await PostDocumentAsync(fileName: "macro.docm", contentType: "application/msword");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("file", out _));
    }

    [Fact]
    public async Task Post_With_Invalid_EffectiveDate_Returns_400()
    {
        var response = await PostDocumentAsync(effectiveDate: "not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("effectiveDate", out _));
    }

    [Fact]
    public async Task Delete_Deactivates_Document_And_Excludes_It_From_Default_List()
    {
        var id = await CreateDocumentAsync("Document To Deactivate");

        var deleteResponse = await _client.DeleteAsync($"/api/knowledge-base/documents/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var defaultList = await GetDocumentsAsync();
        Assert.DoesNotContain(defaultList, d => d.GetProperty("id").GetGuid() == id);

        var fullList = await GetDocumentsAsync(includeDeactivated: true);
        var deactivated = fullList.Single(d => d.GetProperty("id").GetGuid() == id);
        Assert.Equal("Deactivated", deactivated.GetProperty("ingestionStatus").GetString());
    }

    [Fact]
    public async Task Delete_Keeps_Active_Documents_Listed()
    {
        var keptId = await CreateDocumentAsync("Document That Stays");
        var removedId = await CreateDocumentAsync("Document That Goes");

        var deleteResponse = await _client.DeleteAsync($"/api/knowledge-base/documents/{removedId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var defaultList = await GetDocumentsAsync();
        Assert.Contains(defaultList, d => d.GetProperty("id").GetGuid() == keptId);
        Assert.DoesNotContain(defaultList, d => d.GetProperty("id").GetGuid() == removedId);
    }

    [Fact]
    public async Task Delete_Is_Idempotent()
    {
        var id = await CreateDocumentAsync("Deactivate Twice");

        var first = await _client.DeleteAsync($"/api/knowledge-base/documents/{id}");
        var second = await _client.DeleteAsync($"/api/knowledge-base/documents/{id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task Delete_Unknown_Id_Returns_404()
    {
        var response = await _client.DeleteAsync($"/api/knowledge-base/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreateDocumentAsync(string title)
    {
        var response = await PostDocumentAsync(title: title);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<IReadOnlyList<JsonElement>> GetDocumentsAsync(bool includeDeactivated = false)
    {
        var uri = includeDeactivated
            ? "/api/knowledge-base/documents?includeDeactivated=true"
            : "/api/knowledge-base/documents";
        var response = await _client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private Task<HttpResponseMessage> PostDocumentAsync(
        string title = "Test Reference Document",
        string fileName = "permit-application.pdf",
        string contentType = "application/pdf",
        string effectiveDate = "2026-01-15",
        string? omitField = null)
    {
        var form = BuildForm(
            includeFile: true, title, fileName, contentType, effectiveDate, omitField);
        return _client.PostAsync("/api/knowledge-base/documents", form);
    }

    private static MultipartFormDataContent BuildForm(
        bool includeFile,
        string title = "Test Reference Document",
        string fileName = "permit-application.pdf",
        string contentType = "application/pdf",
        string effectiveDate = "2026-01-15",
        string? omitField = null)
    {
        var form = new MultipartFormDataContent();

        if (includeFile)
        {
            var fileContent = new ByteArrayContent(PdfBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(fileContent, "file", fileName);
        }

        void AddField(string name, string value)
        {
            if (!string.Equals(name, omitField, StringComparison.OrdinalIgnoreCase))
            {
                form.Add(new StringContent(value), name);
            }
        }

        AddField("title", title);
        AddField("department", "Engineering");
        AddField("documentType", "PermitForm");
        AddField("permitType", "FloodplainDevelopment");
        AddField("version", "2026.1");
        AddField("effectiveDate", effectiveDate);
        AddField("sourceUrl", "https://www.harriscountytx.gov/permits/form.pdf");

        return form;
    }
}
