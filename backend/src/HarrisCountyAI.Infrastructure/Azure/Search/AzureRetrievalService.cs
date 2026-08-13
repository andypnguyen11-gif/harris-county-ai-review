using System.Diagnostics;
using Azure.Search.Documents.Models;
using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// <see cref="IRetrievalService"/> backed by Azure AI Search: embeds the query,
/// runs a hybrid (keyword + vector) search over the chunk index, and maps the
/// hits back to <see cref="RetrievedChunk"/> values. The query mode and default
/// result count come from <see cref="RetrievalOptions"/>; hybrid is the default
/// because keyword matching catches exact identifiers (section numbers, form
/// numbers) that pure vector similarity blurs.
/// </summary>
/// <remarks>
/// Every query this service issues carries a mandatory scope filter. County
/// requests get the corpus filter <c>sourceType eq 'KnowledgeBase'</c>, which
/// keeps case-uploaded documents out of corpus retrieval; case requests
/// require a case id and get <c>sourceType eq 'CaseDocument' and caseId eq
/// '&lt;id&gt;'</c>, which keeps the corpus and every other case out of case
/// retrieval. Neither filter is optional or caller-controlled (see
/// docs/architecture/rag-architecture.md).
/// </remarks>
public sealed class AzureRetrievalService : IRetrievalService
{
    /// <summary>
    /// How much wider than the requested result count the search pool is when
    /// no reranker is configured.
    /// </summary>
    /// <remarks>
    /// The gateway maps the query size to <c>KNearestNeighborsCount</c> as well
    /// as the result count, so this value is also how many candidates the
    /// vector half of a hybrid query nominates before fusion. Sizing that pool
    /// to the requested count starves fusion: a chunk the vector half does not
    /// nominate cannot appear in the fused results no matter how strongly it
    /// matches on keywords, which drops obviously relevant passages from small
    /// result sets. Retrieving a wider pool and trimming after fusion costs one
    /// larger search and no extra model tokens.
    /// </remarks>
    public const int CandidatePoolMultiplier = 3;

    private readonly ISearchQueryGateway _gateway;
    private readonly IEmbeddingService _embeddingService;
    private readonly RetrievalOptions _options;
    private readonly IRerankingService? _rerankingService;
    private readonly RerankingOptions _rerankingOptions;
    private readonly ILogger<AzureRetrievalService> _logger;

    public AzureRetrievalService(
        ISearchQueryGateway gateway,
        IEmbeddingService embeddingService,
        IOptions<RetrievalOptions>? options = null,
        ILogger<AzureRetrievalService>? logger = null,
        IRerankingService? rerankingService = null,
        IOptions<RerankingOptions>? rerankingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(embeddingService);

        _gateway = gateway;
        _embeddingService = embeddingService;
        _options = options?.Value ?? new RetrievalOptions();
        _rerankingService = rerankingService;
        _rerankingOptions = rerankingOptions?.Value ?? new RerankingOptions();
        _logger = logger ?? NullLogger<AzureRetrievalService>.Instance;
    }

    private bool RerankingActive => _rerankingService is not null && _rerankingOptions.Enabled;

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

        if (request.Scope == SourceType.Case && (request.CaseId is null || request.CaseId == Guid.Empty))
        {
            throw new ArgumentException(
                "Case-scoped retrieval requires a case id; without one the query cannot be "
                + "isolated to a single case.",
                nameof(request));
        }

        var topK = request.TopK ?? _options.DefaultTopK;

        // Always retrieve a wider candidate pool than the caller asked for: the
        // reranker's configured pool when it is active, a multiple of the
        // requested count otherwise. The surplus is trimmed away after fusion.
        var searchSize = RerankingActive
            ? Math.Clamp(_rerankingOptions.CandidatePoolSize, topK, RetrievalRequest.MaxTopK)
            : Math.Clamp(topK * CandidatePoolMultiplier, topK, RetrievalRequest.MaxTopK);

        var embeddingStopwatch = Stopwatch.StartNew();
        var embeddings = await _embeddingService.EmbedAsync([request.Query], cancellationToken);
        embeddingStopwatch.Stop();
        if (embeddings.Count == 0)
        {
            throw new InvalidOperationException("The embedding service returned no embedding for the query.");
        }

        var scopeFilter = BuildFilter(request);
        var query = new ChunkSearchQuery
        {
            SearchText = _options.Mode == RetrievalMode.Hybrid ? request.Query : null,
            Vector = embeddings[0].Vector,
            Filter = scopeFilter,
            Size = searchSize,
        };

        var searchStopwatch = Stopwatch.StartNew();
        var hits = await _gateway.SearchAsync(query, cancellationToken);
        searchStopwatch.Stop();

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
            "{Scope} retrieval ({Mode}) returned {ChunkCount} chunks from a pool of {PoolSize} for a "
            + "requested {RequestedCount} "
            + "(scores {TopScore:F4}..{LowScore:F4}; embedding {EmbeddingMs} ms, search {SearchMs} ms).",
            request.Scope,
            _options.Mode,
            chunks.Count,
            searchSize,
            topK,
            chunks.Count > 0 ? chunks[0].Score : 0d,
            chunks.Count > 0 ? chunks[^1].Score : 0d,
            embeddingStopwatch.ElapsedMilliseconds,
            searchStopwatch.ElapsedMilliseconds);

        if (!RerankingActive || chunks.Count == 0)
        {
            // The pool was widened to give fusion enough candidates to work
            // with; the caller still gets only what it asked for.
            return chunks.Count > topK ? chunks[..topK] : chunks;
        }

        // The reranking service fails open: on any reranking problem it
        // returns the leading candidates in hybrid order, so retrieval never
        // fails because of the reranker.
        return await _rerankingService!.RerankAsync(
            new RerankingRequest
            {
                Query = request.Query,
                Candidates = chunks,
                TopN = topK,
                ScopeFilter = scopeFilter,
            },
            cancellationToken);
    }

    /// <summary>
    /// Builds the mandatory OData scope filter for a retrieval request:
    /// the corpus filter for county requests, the case filter for
    /// case-scoped requests.
    /// </summary>
    internal static string BuildFilter(RetrievalRequest request)
        => request.Scope == SourceType.Case ? BuildCaseFilter(request) : BuildCorpusFilter(request);

    /// <summary>
    /// Builds the OData filter for a case-scoped query: only chunks tagged
    /// <see cref="IndexSourceTypes.CaseDocument"/> that belong to exactly the
    /// requested case. Both clauses are mandatory — this is the isolation
    /// boundary that keeps one case's evidence out of every other case's
    /// answers.
    /// </summary>
    internal static string BuildCaseFilter(RetrievalRequest request)
        => $"{SearchIndexDefinition.Fields.SourceType} eq '{IndexSourceTypes.CaseDocument}'"
            + $" and {SearchIndexDefinition.Fields.CaseId} eq '{request.CaseId!.Value:D}'";

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
