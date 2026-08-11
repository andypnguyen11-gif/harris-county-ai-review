using System.Diagnostics;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// <see cref="IRerankingService"/> backed by Azure AI Search semantic ranking:
/// re-issues the query as a semantic keyword search scoped to exactly the
/// candidate chunks, then reorders the candidates by the reranker scores the
/// service reports.
/// </summary>
/// <remarks>
/// Fails open by design. Semantic ranking is a service-tier capability the
/// dev/free tier lacks, so when reranking is disabled in configuration or the
/// service rejects the semantic query, the candidates come back in their
/// original hybrid order (trimmed to TopN) and retrieval proceeds normally.
/// The scoping filter always retains the <c>sourceType eq 'KnowledgeBase'</c>
/// clause — reranking never widens a query beyond the corpus.
/// </remarks>
public sealed class AzureSemanticRerankingService : IRerankingService
{
    private readonly ISearchQueryGateway _gateway;
    private readonly RerankingOptions _options;
    private readonly ILogger<AzureSemanticRerankingService> _logger;

    public AzureSemanticRerankingService(
        ISearchQueryGateway gateway,
        IOptions<RerankingOptions>? options = null,
        ILogger<AzureSemanticRerankingService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        _gateway = gateway;
        _options = options?.Value ?? new RerankingOptions();
        _logger = logger ?? NullLogger<AzureSemanticRerankingService>.Instance;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        RerankingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidates);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("The reranking query must not be empty.", nameof(request));
        }

        if (request.TopN < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.TopN, "TopN must be at least 1.");
        }

        if (request.Candidates.Count == 0)
        {
            return [];
        }

        if (!_options.Enabled)
        {
            _logger.LogDebug("Semantic reranking is disabled; keeping hybrid order.");
            return Trim(request);
        }

        IReadOnlyList<ChunkSearchHit> hits;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            hits = await _gateway.SearchAsync(BuildRerankQuery(request), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Fail open: a reranking outage (wrong tier, missing semantic
            // configuration, transient service error) must not fail retrieval.
            _logger.LogWarning(
                exception,
                "Semantic reranking failed after {ElapsedMs} ms; falling back to hybrid order.",
                stopwatch.ElapsedMilliseconds);
            return Trim(request);
        }

        stopwatch.Stop();
        var reranked = OrderByRerankerScore(request, hits);
        _logger.LogInformation(
            "Semantic reranking scored {ScoredCount} of {CandidateCount} candidates and kept {KeptCount} "
            + "in {ElapsedMs} ms.",
            reranked.Count(chunk => chunk.RerankerScore is not null),
            request.Candidates.Count,
            reranked.Count,
            stopwatch.ElapsedMilliseconds);
        return reranked;
    }

    /// <summary>
    /// Builds the semantic query that rescores exactly the candidate chunks:
    /// keyword-only (the candidates were already vector-matched), scoped by a
    /// <c>search.in</c> filter over their chunk ids plus the mandatory corpus
    /// filter.
    /// </summary>
    internal ChunkSearchQuery BuildRerankQuery(RerankingRequest request) => new()
    {
        SearchText = request.Query,
        Vector = null,
        Filter = BuildCandidateFilter(request.Candidates),
        Size = request.Candidates.Count,
        UseSemanticRanking = true,
        SemanticConfigurationName = _options.SemanticConfigurationName,
    };

    internal static string BuildCandidateFilter(IReadOnlyList<RetrievedChunk> candidates)
    {
        var ids = string.Join(",", candidates.Select(candidate =>
            AzureRetrievalService.EscapeODataString(candidate.ChunkId)));
        return $"{SearchIndexDefinition.Fields.SourceType} eq '{IndexSourceTypes.KnowledgeBase}'"
            + $" and search.in({SearchIndexDefinition.Fields.ChunkId}, '{ids}', ',')";
    }

    /// <summary>
    /// Reorders the candidates by reranker score (best first). Candidates the
    /// semantic query did not score — e.g. dropped by the ranker — keep their
    /// original relative order after every scored candidate.
    /// </summary>
    private static IReadOnlyList<RetrievedChunk> OrderByRerankerScore(
        RerankingRequest request,
        IReadOnlyList<ChunkSearchHit> hits)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var hit in hits)
        {
            if (hit.RerankerScore is { } score
                && hit.Document.TryGetValue(SearchIndexDefinition.Fields.ChunkId, out var value)
                && value is string chunkId)
            {
                scores[chunkId] = score;
            }
        }

        return request.Candidates
            .Select(candidate => scores.TryGetValue(candidate.ChunkId, out var score)
                ? candidate with { RerankerScore = score }
                : candidate)
            .OrderByDescending(candidate => candidate.RerankerScore is not null)
            .ThenByDescending(candidate => candidate.RerankerScore ?? 0d)
            .Take(request.TopN)
            .ToList();
    }

    private static IReadOnlyList<RetrievedChunk> Trim(RerankingRequest request)
        => request.Candidates.Take(request.TopN).ToList();
}
