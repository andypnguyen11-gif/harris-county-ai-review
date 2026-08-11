using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Search.Indexing;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// <see cref="IDocumentIndexService"/> backed by Azure AI Search. Maps
/// <see cref="IndexableChunk"/> instances onto the schema defined by
/// <see cref="SearchIndexDefinition"/> and talks to the service through the
/// <see cref="ISearchIndexGateway"/> seam.
/// </summary>
public sealed class AzureDocumentIndexService : IDocumentIndexService
{
    private readonly ISearchIndexGateway _gateway;
    private readonly SearchOptions _options;
    private readonly RerankingOptions _rerankingOptions;

    public AzureDocumentIndexService(
        ISearchIndexGateway gateway,
        IOptions<SearchOptions> options,
        IOptions<RerankingOptions>? rerankingOptions = null)
    {
        _gateway = gateway;
        _options = options.Value;
        _rerankingOptions = rerankingOptions?.Value ?? new RerankingOptions();
    }

    /// <summary>
    /// Deploys the index schema. The semantic ranking configuration is
    /// included only when reranking is enabled, because deploying it to a
    /// service tier without semantic ranker fails; enabling it later is an
    /// in-place index update, not a re-creation.
    /// </summary>
    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
        => await _gateway.CreateOrUpdateIndexAsync(
            SearchIndexDefinition.Build(
                _options.IndexName,
                includeSemanticRanking: _rerankingOptions.Enabled),
            cancellationToken);

    public async Task IndexAsync(IReadOnlyList<IndexableChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count == 0)
        {
            return;
        }

        var documents = chunks.Select(ToSearchDocument).ToList();
        await _gateway.UploadDocumentsAsync(documents, cancellationToken);
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var filter = DocumentIdFilter(documentId);
        var chunkIds = await _gateway.FindChunkIdsAsync(filter, cancellationToken);

        if (chunkIds.Count == 0)
        {
            return;
        }

        await _gateway.DeleteDocumentsAsync(chunkIds, cancellationToken);
    }

    /// <summary>OData filter selecting every chunk of one document.</summary>
    internal static string DocumentIdFilter(Guid documentId)
        => $"{SearchIndexDefinition.Fields.DocumentId} eq '{documentId:D}'";

    internal static SearchDocument ToSearchDocument(IndexableChunk chunk)
    {
        if (!IndexSourceTypes.IsValid(chunk.SourceType))
        {
            throw new ArgumentException(
                $"Unknown source type '{chunk.SourceType}' on chunk '{chunk.ChunkId}'. " +
                $"Expected '{IndexSourceTypes.KnowledgeBase}' or '{IndexSourceTypes.CaseDocument}'.",
                nameof(chunk));
        }

        if (chunk.Embedding.Length != SearchIndexDefinition.EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"Chunk '{chunk.ChunkId}' has an embedding of length {chunk.Embedding.Length}; " +
                $"the index requires {SearchIndexDefinition.EmbeddingDimensions} dimensions.",
                nameof(chunk));
        }

        return new SearchDocument
        {
            [SearchIndexDefinition.Fields.ChunkId] = chunk.ChunkId,
            [SearchIndexDefinition.Fields.DocumentId] = chunk.DocumentId.ToString("D"),
            [SearchIndexDefinition.Fields.SourceType] = chunk.SourceType,
            [SearchIndexDefinition.Fields.Title] = chunk.Title,
            [SearchIndexDefinition.Fields.Department] = chunk.Department,
            [SearchIndexDefinition.Fields.PermitType] = chunk.PermitType,
            [SearchIndexDefinition.Fields.DocumentType] = chunk.DocumentType,
            [SearchIndexDefinition.Fields.Section] = chunk.Section,
            [SearchIndexDefinition.Fields.Page] = chunk.PageNumber,
            [SearchIndexDefinition.Fields.EffectiveDate] = chunk.EffectiveDate,
            [SearchIndexDefinition.Fields.SourceUrl] = chunk.SourceUrl,
            [SearchIndexDefinition.Fields.Text] = chunk.Text,
            [SearchIndexDefinition.Fields.Embedding] = chunk.Embedding,
            [SearchIndexDefinition.Fields.CaseId] = chunk.CaseId?.ToString("D"),
        };
    }
}
