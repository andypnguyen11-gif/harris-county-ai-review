using Azure.Search.Documents.Models;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>One search result: the raw indexed document and its relevance scores.</summary>
/// <param name="Document">The indexed chunk document as returned by the service.</param>
/// <param name="Score">Relevance score reported by the service; null when the service omits one.</param>
/// <param name="RerankerScore">Semantic reranker score (0–4); null unless the query used semantic ranking.</param>
public sealed record ChunkSearchHit(SearchDocument Document, double? Score, double? RerankerScore = null);
