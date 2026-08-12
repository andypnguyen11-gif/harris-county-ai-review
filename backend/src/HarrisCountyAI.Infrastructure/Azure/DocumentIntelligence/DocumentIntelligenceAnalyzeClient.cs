using Azure;
using Azure.AI.DocumentIntelligence;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// <see cref="IDocumentIntelligenceAnalyzeClient"/> backed by the Azure SDK's
/// <see cref="DocumentIntelligenceClient"/>. Kept free of mapping logic so the
/// SDK round trip is the only untestable piece; SDK failures are translated so
/// callers see "document extraction is unavailable" rather than an endpoint
/// URI.
/// </summary>
public sealed class DocumentIntelligenceAnalyzeClient : IDocumentIntelligenceAnalyzeClient
{
    private readonly DocumentIntelligenceClient _client;
    private readonly ILogger<DocumentIntelligenceAnalyzeClient> _logger;

    public DocumentIntelligenceAnalyzeClient(
        DocumentIntelligenceClient client,
        ILogger<DocumentIntelligenceAnalyzeClient>? logger = null)
    {
        _client = client;
        _logger = logger ?? NullLogger<DocumentIntelligenceAnalyzeClient>.Instance;
    }

    public Task<AnalyzeResult> AnalyzeAsync(
        string modelId,
        BinaryData content,
        IReadOnlyCollection<DocumentAnalysisFeature> features,
        CancellationToken cancellationToken)
        => AzureOperationExecutor.ExecuteAsync(
            ExternalServiceNames.DocumentIntelligence,
            "analyze",
            token => AnalyzeCoreAsync(modelId, content, features, token),
            cancellationToken,
            _logger);

    private async Task<AnalyzeResult> AnalyzeCoreAsync(
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
