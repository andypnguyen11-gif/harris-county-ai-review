using Azure.AI.DocumentIntelligence;
using HarrisCountyAI.Application.Documents.Extraction;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// <see cref="IDocumentExtractionService"/> backed by Azure AI Document
/// Intelligence. Runs the configured layout model with the key/value pairs
/// add-on (selection marks are part of the layout output) and maps the result
/// with <see cref="AnalyzeResultMapper"/>.
/// </summary>
public sealed class AzureDocumentExtractionService : IDocumentExtractionService
{
    private readonly IDocumentIntelligenceAnalyzeClient _analyzeClient;
    private readonly AnalyzeResultMapper _mapper;
    private readonly DocumentIntelligenceOptions _options;

    public AzureDocumentExtractionService(
        IDocumentIntelligenceAnalyzeClient analyzeClient,
        AnalyzeResultMapper mapper,
        IOptions<DocumentIntelligenceOptions> options)
    {
        _analyzeClient = analyzeClient;
        _mapper = mapper;
        _options = options.Value;
    }

    public async Task<ExtractedDocument> ExtractAsync(Guid documentId, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var data = await BinaryData.FromStreamAsync(content, cancellationToken);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var result = await _analyzeClient.AnalyzeAsync(
                _options.ModelId,
                data,
                [DocumentAnalysisFeature.KeyValuePairs],
                linked.Token);

            return _mapper.Map(documentId, result);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Document Intelligence analysis of document '{documentId}' timed out after {_options.TimeoutSeconds} seconds.");
        }
    }
}
