using System.Net;
using System.Net.Http.Headers;
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
/// Exercises POST /api/knowledge-base/documents/{id}/ingest end to end:
/// upload through the API (real SQL Server and Azurite blob storage), then run
/// the pipeline with faked Azure extraction, embedding, and indexing services.
/// </summary>
public class KnowledgeBaseIngestionApiTests : IClassFixture<SqlServerTestDatabase>, IAsyncLifetime
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 ingestion pipeline test content"u8.ToArray();

    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _knowledgeBaseContainerName = $"test-kb-ingest-{Guid.NewGuid():N}"[..36];

    private readonly StubExtractionService _extraction = new();
    private readonly StubEmbeddingService _embeddings = new();
    private readonly RecordingIndexService _index = new();

    public KnowledgeBaseIngestionApiTests(SqlServerTestDatabase database)
    {
        _factory = new TestApplicationFactory
        {
            ConnectionStringOverride = database.ConnectionString,
            // The pipeline must never call real Azure services from tests;
            // replace the production registrations with in-memory stubs.
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
        _factory.SettingOverrides["BlobStorage:KnowledgeBaseContainerName"] = _knowledgeBaseContainerName;
        _client = _factory.CreateClient().WithToken(
            TestAuthentication.CreateToken(TestAuthentication.AdministratorUsername, ["Administrator"]));
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
    public async Task Ingest_Uploaded_Document_Returns_Succeeded_And_Marks_Document_Ingested()
    {
        var id = await UploadDocumentAsync("Floodplain Management Regulations");

        var response = await _client.PostAsync($"/api/knowledge-base/documents/{id}/ingest", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal(id, root.GetProperty("documentId").GetGuid());
        Assert.Equal("Succeeded", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("chunkCount").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failureReason").ValueKind);

        var document = await GetDocumentAsync(id);
        Assert.Equal("Ingested", document.GetProperty("ingestionStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, document.GetProperty("ingestionDate").ValueKind);
    }

    [Fact]
    public async Task Ingest_Writes_KnowledgeBase_Chunks_With_DeleteThenIndex()
    {
        var id = await UploadDocumentAsync("Corpus Separation Reference");

        var response = await _client.PostAsync($"/api/knowledge-base/documents/{id}/ingest", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(
            ["EnsureIndex", $"Delete:{id}", "Index"],
            _index.Operations.Select(operation => operation.StartsWith("Index:") ? "Index" : operation));

        var batch = Assert.Single(_index.IndexedBatches);
        Assert.All(batch, chunk =>
        {
            Assert.Equal(IndexSourceTypes.KnowledgeBase, chunk.SourceType);
            Assert.Null(chunk.CaseId);
            Assert.Equal(id, chunk.DocumentId);
            Assert.Equal("Corpus Separation Reference", chunk.Title);
            Assert.Equal("Engineering", chunk.Department);
            Assert.Equal("FloodplainDevelopment", chunk.PermitType);
            Assert.Equal(1536, chunk.Embedding.Length);
        });
    }

    [Fact]
    public async Task Ingest_Unknown_Document_Returns_404()
    {
        var response = await _client.PostAsync($"/api/knowledge-base/documents/{Guid.NewGuid()}/ingest", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_Deactivated_Document_Returns_409()
    {
        var id = await UploadDocumentAsync("Deactivated Reference");
        var deleteResponse = await _client.DeleteAsync($"/api/knowledge-base/documents/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var response = await _client.PostAsync($"/api/knowledge-base/documents/{id}/ingest", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Ingest_Failure_Marks_Failed_And_Document_Can_Be_Reprocessed()
    {
        var id = await UploadDocumentAsync("Reprocessed Reference");

        _extraction.ExtractException = new InvalidOperationException("Extraction outage.");
        var failedResponse = await _client.PostAsync($"/api/knowledge-base/documents/{id}/ingest", null);

        Assert.Equal(HttpStatusCode.OK, failedResponse.StatusCode);
        using (var failedBody = JsonDocument.Parse(await failedResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Failed", failedBody.RootElement.GetProperty("status").GetString());
            Assert.Contains("Extraction outage.", failedBody.RootElement.GetProperty("failureReason").GetString());
        }

        var failedDocument = await GetDocumentAsync(id);
        Assert.Equal("Failed", failedDocument.GetProperty("ingestionStatus").GetString());

        _extraction.ExtractException = null;
        var retryResponse = await _client.PostAsync($"/api/knowledge-base/documents/{id}/ingest", null);

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using (var retryBody = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Succeeded", retryBody.RootElement.GetProperty("status").GetString());
        }

        var ingestedDocument = await GetDocumentAsync(id);
        Assert.Equal("Ingested", ingestedDocument.GetProperty("ingestionStatus").GetString());
    }

    private async Task<Guid> UploadDocumentAsync(string title)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(PdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "file", "reference.pdf");
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent("Engineering"), "department");
        form.Add(new StringContent("Regulation"), "documentType");
        form.Add(new StringContent("FloodplainDevelopment"), "permitType");

        var response = await _client.PostAsync("/api/knowledge-base/documents", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetDocumentAsync(Guid id)
    {
        var response = await _client.GetAsync("/api/knowledge-base/documents?includeDeactivated=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == id).Clone();
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
                        Text = "1. Elevation Requirements\nAll structures must be elevated at least "
                            + "one foot above the base flood elevation shown on the effective FIRM.",
                    },
                ],
                RawText = "All structures must be elevated.",
                ModelId = "stub-model",
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
        public List<string> Operations { get; } = [];

        public List<IReadOnlyList<IndexableChunk>> IndexedBatches { get; } = [];

        public Task EnsureIndexAsync(CancellationToken cancellationToken = default)
        {
            Operations.Add("EnsureIndex");
            return Task.CompletedTask;
        }

        public Task IndexAsync(IReadOnlyList<IndexableChunk> chunks, CancellationToken cancellationToken = default)
        {
            Operations.Add($"Index:{chunks.Count}");
            IndexedBatches.Add(chunks);
            return Task.CompletedTask;
        }

        public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            Operations.Add($"Delete:{documentId}");
            return Task.CompletedTask;
        }
    }
}
