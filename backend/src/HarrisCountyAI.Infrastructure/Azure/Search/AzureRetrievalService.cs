using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// <see cref="IRetrievalService"/> backed by Azure AI Search: embeds the query,
/// runs a vector similarity search over the chunk index, and maps the hits back
/// to <see cref="RetrievedChunk"/> values.
/// </summary>
/// <remarks>
/// Every query this service issues carries the corpus filter
/// <c>sourceType eq 'KnowledgeBase'</c>. That filter is not optional and not
/// caller-controlled — it is what keeps case-uploaded documents out of corpus
/// retrieval (see docs/architecture/rag-architecture.md).
/// </remarks>
public sealed class AzureRetrievalService : IRetrievalService
{
    private readonly ISearchQueryGateway _gateway;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<AzureRetrievalService> _logger;

    public AzureRetrievalService(
        ISearchQueryGateway gateway,
        IEmbeddingService embeddingService,
        ILogger<AzureRetrievalService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(embeddingService);

        _gateway = gateway;
        _embeddingService = embeddingService;
        _logger = logger ?? NullLogger<AzureRetrievalService>.Instance;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("The retrieval query must not be empty.", nameof(request));
        }

        if (request.TopK is < 1 or > RetrievalRequest.MaxTopK)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.TopK,
                $"TopK must be between 1 and {RetrievalRequest.MaxTopK}.");
        }

        var embeddings = await _embeddingService.EmbedAsync([request.Query], cancellationToken);
        if (embeddings.Count == 0)
        {
            throw new InvalidOperationException("The embedding service returned no embedding for the query.");
        }

        var query = new ChunkSearchQuery
        {
            Vector = embeddings[0].Vector,
            Filter = BuildCorpusFilter(request),
            Size = request.TopK,
        };

        var hits = await _gateway.SearchAsync(query, cancellationToken);
        var chunks = new List<RetrievedChunk>(hits.Count);
        foreach (var hit in hits)
        {
            var chunk = TryMapChunk(hit);
            if (chunk is not null)
            {
                chunks.Add(chunk);
            }
        }

        _logger.LogInformation(
            "Corpus retrieval returned {ChunkCount} of {RequestedCount} requested chunks.",
            chunks.Count,
            request.TopK);

        return chunks;
    }

    /// <summary>
    /// Builds the OData filter for a corpus query. Always scopes to
    /// knowledge-base chunks; optional metadata filters narrow further.
    /// </summary>
    internal static string BuildCorpusFilter(RetrievalRequest request)
    {
        var clauses = new List<string>
        {
            $"{SearchIndexDefinition.Fields.SourceType} eq '{IndexSourceTypes.KnowledgeBase}'",
        };

        AddEqualsClause(clauses, SearchIndexDefinition.Fields.Department, request.Department);
        AddEqualsClause(clauses, SearchIndexDefinition.Fields.PermitType, request.PermitType);
        AddEqualsClause(clauses, SearchIndexDefinition.Fields.DocumentType, request.DocumentType);

        return string.Join(" and ", clauses);
    }

    private static void AddEqualsClause(List<string> clauses, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            clauses.Add($"{field} eq '{EscapeODataString(value)}'");
        }
    }

    /// <summary>Escapes a literal for use inside an OData single-quoted string.</summary>
    internal static string EscapeODataString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>
    /// Maps one hit to a <see cref="RetrievedChunk"/>, or null when the indexed
    /// document is missing a required field (which indicates index corruption,
    /// not a caller error — logged and skipped rather than thrown).
    /// </summary>
    private RetrievedChunk? TryMapChunk(ChunkSearchHit hit)
    {
        var document = hit.Document;
        var chunkId = GetString(document, SearchIndexDefinition.Fields.ChunkId);
        var text = GetString(document, SearchIndexDefinition.Fields.Text);
        var title = GetString(document, SearchIndexDefinition.Fields.Title);
        var documentIdValue = GetString(document, SearchIndexDefinition.Fields.DocumentId);

        if (chunkId is null || text is null || title is null
            || !Guid.TryParse(documentIdValue, out var documentId))
        {
            _logger.LogWarning(
                "Skipping retrieved chunk '{ChunkId}' with missing or malformed required fields.",
                chunkId ?? "(no id)");
            return null;
        }

        return new RetrievedChunk
        {
            ChunkId = chunkId,
            DocumentId = documentId,
            Text = text,
            Title = title,
            Section = GetString(document, SearchIndexDefinition.Fields.Section),
            Page = GetInt(document, SearchIndexDefinition.Fields.Page),
            Department = GetString(document, SearchIndexDefinition.Fields.Department),
            PermitType = GetString(document, SearchIndexDefinition.Fields.PermitType),
            DocumentType = GetString(document, SearchIndexDefinition.Fields.DocumentType),
            EffectiveDate = GetDate(document, SearchIndexDefinition.Fields.EffectiveDate),
            SourceUrl = GetString(document, SearchIndexDefinition.Fields.SourceUrl),
            Score = hit.Score ?? 0d,
        };
    }

    private static string? GetString(SearchDocument document, string field)
        => document.TryGetValue(field, out var value) && value is string text ? text : null;

    private static int? GetInt(SearchDocument document, string field)
        => document.TryGetValue(field, out var value) && value is int number ? number : null;

    private static DateTimeOffset? GetDate(SearchDocument document, string field)
        => document.TryGetValue(field, out var value) && value is DateTimeOffset date ? date : null;
}
