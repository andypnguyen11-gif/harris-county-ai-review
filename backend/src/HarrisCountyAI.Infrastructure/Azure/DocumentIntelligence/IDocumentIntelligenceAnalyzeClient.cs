using Azure.AI.DocumentIntelligence;

namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// Thin seam over the Azure Document Intelligence analyze round trip so
/// <see cref="AzureDocumentExtractionService"/> can be tested without real
/// service calls. The default implementation is
/// <see cref="DocumentIntelligenceAnalyzeClient"/>.
/// </summary>
public interface IDocumentIntelligenceAnalyzeClient
{
    /// <summary>
    /// Runs the given analysis model over <paramref name="content"/> and waits
    /// for the completed <see cref="AnalyzeResult"/>.
    /// </summary>
    Task<AnalyzeResult> AnalyzeAsync(
        string modelId,
        BinaryData content,
        IReadOnlyCollection<DocumentAnalysisFeature> features,
        CancellationToken cancellationToken);
}
