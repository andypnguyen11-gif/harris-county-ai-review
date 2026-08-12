using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using HarrisCountyAI.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// Exercises POST /api/cases/{caseId}/documents/{documentId}/process — the
/// trigger that takes an uploaded document through extraction, normalization,
/// persistence, and case-scoped indexing. Upload and storage are real (SQL
/// Server and Azurite); only the Azure AI dependencies are stubbed.
/// </summary>
public class DocumentProcessingApiTests : IClassFixture<SqlServerTestDatabase>, IAsyncLifetime
{
    /// <summary>A minimal but structurally real single-page PDF; extraction is stubbed, so only the bytes round-tripping matters.</summary>
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "trailer<</Root 1 0 R>>\n" +
        "%%EOF\n");

    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _containerName = $"test-doc-process-{Guid.NewGuid():N}"[..40];

    private readonly StubExtractionService _extraction = new();
    private readonly StubEmbeddingService _embeddings = new();
    private readonly RecordingIndexService _index = new();

    public DocumentProcessingApiTests(SqlServerTestDatabase database)
    {
        _factory = new TestApplicationFactory
        {
            ConnectionStringOverride = database.ConnectionString,
            // The pipeline must never reach a real Azure service from tests.
            TestServices = services =>
            {
                services.RemoveAll<IDocumentExtractionService>();
                services.AddSingleton<IDocumentExtractionService>(_extraction);
                services.RemoveAll<IEmbeddingService>();
                services.AddSingleton<IEmbeddingService>(_embeddings);
                services.RemoveAll<IDocumentIndexService>();
                services.AddSingleton<IDocumentIndexService>(_index);
            },
        };
        _factory.BlobStorageOverrides["CaseDocumentsContainerName"] = _containerName;
        _client = _factory.CreateClient().WithToken(TestAuthentication.CreateToken(roles: ["Reviewer"]));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();

        var blobs = BlobStorageServiceExtensions.CreateBlobServiceClient("UseDevelopmentStorage=true");
        await blobs.GetBlobContainerClient(_containerName).DeleteIfExistsAsync();
    }

    // --- Success -----------------------------------------------------------

    [Fact]
    public async Task Process_Returns_200_With_The_Document_Normalized_And_No_Failure_Reason()
    {
        var (caseId, documentId) = await UploadAsync();

        var response = await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var document = body.GetProperty("document");
        Assert.Equal(documentId, document.GetProperty("id").GetGuid());
        Assert.Equal(caseId, document.GetProperty("caseId").GetGuid());
        Assert.Equal("Normalized", document.GetProperty("processingStatus").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("failureReason").ValueKind);
    }

    [Fact]
    public async Task Process_Advances_The_Document_Status_Read_Back_From_The_Api()
    {
        var (caseId, documentId) = await UploadAsync();

        // Before: the upload alone leaves the document unprocessed.
        Assert.Equal("Uploaded", (await GetDocumentAsync(caseId, documentId)).GetProperty("processingStatus").GetString());

        var response = await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal("Normalized", (await GetDocumentAsync(caseId, documentId)).GetProperty("processingStatus").GetString());
    }

    [Fact]
    public async Task Process_Indexes_The_Document_As_Case_Scoped_Evidence()
    {
        var (caseId, documentId) = await UploadAsync();

        var response = await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var batch = Assert.Single(_index.IndexedBatches);
        Assert.NotEmpty(batch);
        Assert.All(batch, chunk =>
        {
            // Tagged as case evidence, never as reference corpus.
            Assert.Equal(IndexSourceTypes.CaseDocument, chunk.SourceType);
            Assert.Equal(caseId, chunk.CaseId);
            Assert.Equal(documentId, chunk.DocumentId);
        });
    }

    // --- Failure -----------------------------------------------------------

    [Fact]
    public async Task A_Pipeline_Failure_Returns_200_Reporting_The_Terminal_Failed_Status_And_Reason()
    {
        var (caseId, documentId) = await UploadAsync();
        _extraction.ExtractException = new InvalidOperationException(
            "The file could not be analyzed: unexpected end of stream.");

        var response = await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        // Not a 5xx: the request was handled and its outcome durably recorded,
        // which a client must be able to tell apart from a call that never landed.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Failed", body.GetProperty("document").GetProperty("processingStatus").GetString());
        Assert.Contains("could not be analyzed", body.GetProperty("failureReason").GetString());
    }

    [Fact]
    public async Task A_Failed_Document_Surfaces_As_Failed_On_Its_Own_Endpoint_Rather_Than_Staying_Uploaded()
    {
        var (caseId, documentId) = await UploadAsync();
        _extraction.ExtractException = new InvalidOperationException("Extraction outage.");

        await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        Assert.Equal("Failed", (await GetDocumentAsync(caseId, documentId)).GetProperty("processingStatus").GetString());

        var list = await _client.GetAsync($"/api/cases/{caseId}/documents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = (await ReadJsonAsync(list)).EnumerateArray()
            .Single(d => d.GetProperty("id").GetGuid() == documentId);
        Assert.Equal("Failed", listed.GetProperty("processingStatus").GetString());
    }

    [Fact]
    public async Task A_Failed_Document_Is_Not_Indexed_As_Evidence()
    {
        var (caseId, documentId) = await UploadAsync();
        _extraction.ExtractException = new InvalidOperationException("Extraction outage.");

        await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        Assert.Empty(_index.IndexedBatches);
    }

    [Fact]
    public async Task A_Failed_Document_Can_Be_Reprocessed_Through_The_Same_Endpoint()
    {
        var (caseId, documentId) = await UploadAsync();
        _extraction.ExtractException = new InvalidOperationException("Extraction outage.");

        var failed = await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);
        Assert.Equal("Failed", (await ReadJsonAsync(failed)).GetProperty("document").GetProperty("processingStatus").GetString());

        // The stored file survived the failure, so only the expensive half retries.
        _extraction.ExtractException = null;
        var retried = await _client.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var body = await ReadJsonAsync(retried);
        Assert.Equal("Normalized", body.GetProperty("document").GetProperty("processingStatus").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("failureReason").ValueKind);
    }

    // --- Case scoping ------------------------------------------------------

    [Fact]
    public async Task Process_Unknown_Document_Returns_404_ProblemDetails()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.PostAsync($"/api/cases/{caseId}/documents/{Guid.NewGuid()}/process", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Process_Does_Not_Run_Another_Cases_Document()
    {
        var (ownerCaseId, documentId) = await UploadAsync();
        var otherCaseId = await CreateCaseAsync();

        var response = await _client.PostAsync($"/api/cases/{otherCaseId}/documents/{documentId}/process", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Untouched: the document neither ran nor changed status.
        Assert.Equal("Uploaded", (await GetDocumentAsync(ownerCaseId, documentId)).GetProperty("processingStatus").GetString());
        Assert.Empty(_index.IndexedBatches);
    }

    // --- Authorization -----------------------------------------------------

    [Fact]
    public async Task Process_Rejects_Anonymous_Callers()
    {
        var (caseId, documentId) = await UploadAsync();
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Process_Rejects_An_Authenticated_Caller_Without_A_Case_Work_Role()
    {
        var (caseId, documentId) = await UploadAsync();
        using var outsider = _factory.CreateClient().WithToken(
            TestAuthentication.CreateToken("dev.outsider", ["Clerk"]));

        var response = await outsider.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Denied before the pipeline, not after it.
        Assert.Empty(_index.IndexedBatches);
        Assert.Equal("Uploaded", (await GetDocumentAsync(caseId, documentId)).GetProperty("processingStatus").GetString());
    }

    [Fact]
    public async Task Process_Allows_An_Administrator_Because_RequireReviewer_Admits_Both_Case_Work_Roles()
    {
        var (caseId, documentId) = await UploadAsync();
        using var administrator = _factory.CreateClient().WithToken(
            TestAuthentication.CreateToken(TestAuthentication.AdministratorUsername, ["Administrator"]));

        var response = await administrator.PostAsync($"/api/cases/{caseId}/documents/{documentId}/process", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Helpers -----------------------------------------------------------

    private async Task<Guid> CreateCaseAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/cases", new
        {
            name = "Document Processing Case",
            workflowType = "FloodplainDevelopmentPermit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<(Guid CaseId, Guid DocumentId)> UploadAsync()
    {
        var caseId = await CreateCaseAsync();

        var file = new ByteArrayContent(PdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent
        {
            { file, "file", "permit-application.pdf" },
            { new StringContent("PermitApplication"), "documentType" },
        };

        var response = await _client.PostAsync($"/api/cases/{caseId}/documents", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (caseId, (await ReadJsonAsync(response)).GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> GetDocumentAsync(Guid caseId, Guid documentId)
    {
        var response = await _client.GetAsync($"/api/cases/{caseId}/documents/{documentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class StubExtractionService : IDocumentExtractionService
    {
        public Exception? ExtractException { get; set; }

        public Task<ExtractedDocument> ExtractAsync(Guid documentId, Stream content, CancellationToken cancellationToken)
        {
            if (ExtractException is not null)
            {
                throw ExtractException;
            }

            return Task.FromResult(new ExtractedDocument
            {
                DocumentId = documentId,
                Pages =
                [
                    new ExtractedPage
                    {
                        PageNumber = 1,
                        Text = "Harris County Floodplain Development Permit Application. "
                            + "Owner Name: Jane P. Smith. Property Address: 1420 Cypresswood Drive.",
                    },
                ],
                KeyValuePairs =
                [
                    new ExtractedField { Key = "Owner Name:", Value = "Jane P. Smith", Confidence = 0.97, PageNumber = 1 },
                ],
                RawText = "Harris County Floodplain Development Permit Application.",
                ModelId = "stub-extraction-model",
                ExtractedAt = DateTime.UtcNow,
            });
        }
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EmbeddingResult>>(inputs
                .Select((_, index) => new EmbeddingResult(new float[1536], index, "stub-embedding-model"))
                .ToList());
    }

    private sealed class RecordingIndexService : IDocumentIndexService
    {
        public List<IReadOnlyList<IndexableChunk>> IndexedBatches { get; } = [];

        public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task IndexAsync(IReadOnlyList<IndexableChunk> chunks, CancellationToken cancellationToken = default)
        {
            IndexedBatches.Add(chunks);
            return Task.CompletedTask;
        }

        public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
