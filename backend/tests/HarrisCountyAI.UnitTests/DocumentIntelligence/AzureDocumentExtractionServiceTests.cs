using Azure.AI.DocumentIntelligence;
using HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.DocumentIntelligence;

public class AzureDocumentExtractionServiceTests
{
    private sealed class FakeAnalyzeClient : IDocumentIntelligenceAnalyzeClient
    {
        public string? LastModelId { get; private set; }

        public BinaryData? LastContent { get; private set; }

        public IReadOnlyCollection<DocumentAnalysisFeature>? LastFeatures { get; private set; }

        public AnalyzeResult Result { get; set; } = DocumentIntelligenceModelFactory.AnalyzeResult(
            modelId: "prebuilt-layout",
            content: "Analyzed content");

        public Exception? Exception { get; set; }

        public Task<AnalyzeResult> AnalyzeAsync(
            string modelId,
            BinaryData content,
            IReadOnlyCollection<DocumentAnalysisFeature> features,
            CancellationToken cancellationToken)
        {
            LastModelId = modelId;
            LastContent = content;
            LastFeatures = features;

            return Exception is not null ? Task.FromException<AnalyzeResult>(Exception) : Task.FromResult(Result);
        }
    }

    private static AzureDocumentExtractionService CreateService(FakeAnalyzeClient client, DocumentIntelligenceOptions? options = null) =>
        new(client, new AnalyzeResultMapper(), Options.Create(options ?? new DocumentIntelligenceOptions
        {
            Endpoint = "https://example.cognitiveservices.azure.com/",
            ApiKey = "test-key",
        }));

    [Fact]
    public async Task Analyzes_Content_With_Configured_Model_And_KeyValuePairs_Feature()
    {
        var client = new FakeAnalyzeClient();
        var service = CreateService(client);
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream([1, 2, 3, 4]);

        var extracted = await service.ExtractAsync(documentId, content, CancellationToken.None);

        Assert.Equal("prebuilt-layout", client.LastModelId);
        Assert.Equal([1, 2, 3, 4], client.LastContent!.ToArray());
        Assert.Contains(DocumentAnalysisFeature.KeyValuePairs, client.LastFeatures!);
        Assert.Equal(documentId, extracted.DocumentId);
        Assert.Equal("Analyzed content", extracted.RawText);
    }

    [Fact]
    public async Task Uses_Custom_Model_Id_From_Options()
    {
        var client = new FakeAnalyzeClient();
        var service = CreateService(client, new DocumentIntelligenceOptions
        {
            Endpoint = "https://example.cognitiveservices.azure.com/",
            ApiKey = "test-key",
            ModelId = "custom-model",
        });

        using var content = new MemoryStream([1]);
        await service.ExtractAsync(Guid.NewGuid(), content, CancellationToken.None);

        Assert.Equal("custom-model", client.LastModelId);
    }

    [Fact]
    public async Task Propagates_Analysis_Failures()
    {
        var client = new FakeAnalyzeClient { Exception = new InvalidOperationException("Service unavailable.") };
        var service = CreateService(client);

        using var content = new MemoryStream([1]);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExtractAsync(Guid.NewGuid(), content, CancellationToken.None));
    }
}
