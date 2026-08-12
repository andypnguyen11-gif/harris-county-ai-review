using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using HarrisCountyAI.Infrastructure.Azure.Search;
using HarrisCountyAI.IntegrationTests.Api;
using HarrisCountyAI.IntegrationTests.Persistence;
using HarrisCountyAI.IntegrationTests.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// The shared harness for the MVP end-to-end suite: one application host per
/// test class, wired the way the workflow actually runs — real HTTP through the
/// API, real SQL Server persistence, real Azurite blob storage, and the real
/// normalization, chunking, indexing, retrieval, validation, and question
/// answering services — with only the four external Azure AI dependencies
/// replaced by in-process stand-ins.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here reaches an Azure endpoint. Document Intelligence, the embedding
/// deployment, the chat deployment, and Azure AI Search are each replaced at the
/// narrowest seam available, so everything above them — including
/// <c>AzureDocumentIndexService</c> and <c>AzureRetrievalService</c>, with the
/// scope filters that keep the corpus and case documents apart — is the
/// production code path.
/// </para>
/// <para>
/// Every step in the scenario goes over the wire, including the pipeline step
/// between upload and validation: <see cref="ProcessAsync"/> posts to
/// <c>/api/cases/{caseId}/documents/{documentId}/process</c>, the same
/// endpoint the reviewer's browser calls. Nothing in the suite reaches into
/// the host's service provider to move the workflow along.
/// </para>
/// </remarks>
public abstract class EndToEndTestBase : IAsyncLifetime
{
    /// <summary>A minimal but structurally real single-page PDF; extraction is scripted, so only the bytes round-tripping matters.</summary>
    protected static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\n" +
        "trailer<</Root 1 0 R>>\n" +
        "%%EOF\n");

    private readonly string _caseDocumentsContainer = $"e2e-case-{Guid.NewGuid():N}"[..36];
    private readonly string _knowledgeBaseContainer = $"e2e-kb-{Guid.NewGuid():N}"[..36];

    protected EndToEndTestBase(SqlServerTestDatabase database)
    {
        Factory = new TestApplicationFactory
        {
            ConnectionStringOverride = database.ConnectionString,
            TestServices = services =>
            {
                // Azure AI Document Intelligence.
                services.RemoveAll<IDocumentExtractionService>();
                services.AddSingleton<IDocumentExtractionService>(Extraction);

                // The Azure embedding deployment.
                services.RemoveAll<IEmbeddingService>();
                services.AddSingleton<IEmbeddingService>(Embeddings);

                // The Azure chat deployment.
                services.RemoveAll<ILanguageModelService>();
                services.AddSingleton<ILanguageModelService>(LanguageModel);

                // Azure AI Search, replaced at the gateway seam so the real
                // index and retrieval services — and their scope filters — run.
                services.RemoveAll<ISearchIndexGateway>();
                services.AddSingleton<ISearchIndexGateway>(SearchIndex);
                services.RemoveAll<ISearchQueryGateway>();
                services.AddSingleton<ISearchQueryGateway>(SearchIndex);
            },
        };

        Factory.BlobStorageOverrides["CaseDocumentsContainerName"] = _caseDocumentsContainer;
        Factory.BlobStorageOverrides["KnowledgeBaseContainerName"] = _knowledgeBaseContainer;

        Reviewer = Factory.CreateClient().WithToken(
            TestAuthentication.CreateToken(TestAuthentication.ReviewerUsername, ["Reviewer"]));
        Administrator = Factory.CreateClient().WithToken(
            TestAuthentication.CreateToken(TestAuthentication.AdministratorUsername, ["Administrator"]));
    }

    protected TestApplicationFactory Factory { get; }

    /// <summary>A Reviewer's client — the role that works cases.</summary>
    protected HttpClient Reviewer { get; }

    /// <summary>An Administrator's client, for the knowledge-base endpoints.</summary>
    protected HttpClient Administrator { get; }

    protected ScriptedExtractionService Extraction { get; } = new();

    protected StubEmbeddingService Embeddings { get; } = new();

    protected ScriptedLanguageModelService LanguageModel { get; } = new();

    /// <summary>The chunk index the real indexing and retrieval services read and write.</summary>
    protected InMemorySearchIndex SearchIndex { get; } = new();

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual async Task DisposeAsync()
    {
        Reviewer.Dispose();
        Administrator.Dispose();
        Factory.Dispose();

        var blobs = BlobStorageServiceExtensions.CreateBlobServiceClient("UseDevelopmentStorage=true");
        await blobs.GetBlobContainerClient(_caseDocumentsContainer).DeleteIfExistsAsync();
        await blobs.GetBlobContainerClient(_knowledgeBaseContainer).DeleteIfExistsAsync();
    }

    // --- Case and document steps -------------------------------------------

    protected async Task<Guid> CreateCaseAsync(string name = "Cypresswood Residence")
    {
        var response = await Reviewer.PostAsJsonAsync("/api/cases", new
        {
            name,
            workflowType = "FloodplainDevelopmentPermit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a file to a case over HTTP and returns the new document's id.</summary>
    protected async Task<Guid> UploadAsync(Guid caseId, string fileName, string documentType)
    {
        var file = new ByteArrayContent(PdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var form = new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(documentType), "documentType" },
        };

        var response = await Reviewer.PostAsync($"/api/cases/{caseId}/documents", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Runs extraction, normalization, persistence, and search indexing for an
    /// uploaded document over HTTP, and asserts the run completed, leaving the
    /// document at its terminal <c>Normalized</c> status.
    /// </summary>
    protected async Task ProcessAsync(Guid caseId, Guid documentId)
    {
        var result = await ProcessRequestAsync(caseId, documentId);

        Assert.Equal(JsonValueKind.Null, result.GetProperty("failureReason").ValueKind);
        Assert.Equal("Normalized", result.GetProperty("document").GetProperty("processingStatus").GetString());
    }

    /// <summary>
    /// Runs the pipeline expecting it to fail, and returns the reason the API
    /// reported. A failed run is still a handled request — the endpoint answers
    /// <c>200 OK</c> and reports the terminal <c>Failed</c> status in the body,
    /// which is what distinguishes it from a call that never landed.
    /// </summary>
    protected async Task<string> ProcessExpectingFailureAsync(Guid caseId, Guid documentId)
    {
        var result = await ProcessRequestAsync(caseId, documentId);

        Assert.Equal("Failed", result.GetProperty("document").GetProperty("processingStatus").GetString());
        var reason = result.GetProperty("failureReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason), "A failed run must report why it failed.");
        return reason!;
    }

    private async Task<JsonElement> ProcessRequestAsync(Guid caseId, Guid documentId)
    {
        var response = await Reviewer.PostAsync(
            $"/api/cases/{caseId}/documents/{documentId}/process", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    /// <summary>Uploads a document and runs it through the pipeline with the given extraction result.</summary>
    protected async Task<Guid> SubmitAsync(
        Guid caseId,
        string fileName,
        string documentType,
        Func<Guid, ExtractedDocument> extracted)
    {
        var documentId = await UploadAsync(caseId, fileName, documentType);
        Extraction.Script(documentId, extracted);
        await ProcessAsync(caseId, documentId);
        return documentId;
    }

    protected async Task<JsonElement> GetDocumentAsync(Guid caseId, Guid documentId)
    {
        var response = await Reviewer.GetAsync($"/api/cases/{caseId}/documents/{documentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    // --- Validation steps ---------------------------------------------------

    protected async Task<JsonElement> RunValidationAsync(Guid caseId)
    {
        var response = await Reviewer.PostAsync($"/api/cases/{caseId}/validation", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    /// <summary>The single report item for a requirement, by its label.</summary>
    protected static JsonElement Item(JsonElement report, string requirement) =>
        report.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("requirement").GetString() == requirement);

    protected static string StatusOf(JsonElement report, string requirement) =>
        Item(report, requirement).GetProperty("status").GetString()!;

    // --- Knowledge base steps -----------------------------------------------

    /// <summary>Uploads and ingests a reference-corpus document, so county questions have evidence to retrieve.</summary>
    protected async Task<Guid> IngestKnowledgeDocumentAsync(string title, string text)
    {
        var file = new ByteArrayContent(PdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var form = new MultipartFormDataContent
        {
            { file, "file", "floodplain-regulations.pdf" },
            { new StringContent(title), "title" },
            { new StringContent("Engineering"), "department" },
            { new StringContent("Regulation"), "documentType" },
            { new StringContent("FloodplainDevelopment"), "permitType" },
        };

        var upload = await Administrator.PostAsync("/api/knowledge-base/documents", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var documentId = (await ReadJsonAsync(upload)).GetProperty("id").GetGuid();

        Extraction.Script(documentId, id => new ExtractedDocument
        {
            DocumentId = id,
            Pages = [new ExtractedPage { PageNumber = 1, Text = text }],
            RawText = text,
            ModelId = "stub-extraction-model",
            ExtractedAt = DateTime.UtcNow,
        });

        var ingest = await Administrator.PostAsync($"/api/knowledge-base/documents/{documentId}/ingest", content: null);
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);
        Assert.Equal("Succeeded", (await ReadJsonAsync(ingest)).GetProperty("status").GetString());

        return documentId;
    }

    // --- Question answering steps -------------------------------------------

    protected Task<HttpResponseMessage> AskAsync(object request) =>
        Reviewer.PostAsJsonAsync("/api/questions", request);

    protected async Task<JsonElement> AskSuccessfullyAsync(object request)
    {
        var response = await AskAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    // --- Shared helpers -----------------------------------------------------

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
