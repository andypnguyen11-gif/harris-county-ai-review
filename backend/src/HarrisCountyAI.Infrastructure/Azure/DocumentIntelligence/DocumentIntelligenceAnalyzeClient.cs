using Azure;
using Azure.AI.DocumentIntelligence;

namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// <see cref="IDocumentIntelligenceAnalyzeClient"/> backed by the Azure SDK's
/// <see cref="DocumentIntelligenceClient"/>. Kept free of mapping logic so the
/// SDK round trip is the only untestable piece.
/// </summary>
public sealed class DocumentIntelligenceAnalyzeClient : IDocumentIntelligenceAnalyzeClient
{
    private readonly DocumentIntelligenceClient _client;

    public DocumentIntelligenceAnalyzeClient(DocumentIntelligenceClient client)
    {
        _client = client;
    }

    public async Task<AnalyzeResult> AnalyzeAsync(
        string modelId,
        BinaryData content,
        IReadOnlyCollection<DocumentAnalysisFeature> features,
        CancellationToken cancellationToken)
    {
        var options = new AnalyzeDocumentOptions(modelId, content);
        foreach (var feature in features)
        {
            options.Features.Add(feature);
        }

        var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
        return operation.Value;
    }
}
