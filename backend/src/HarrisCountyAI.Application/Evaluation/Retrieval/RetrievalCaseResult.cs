namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>
/// What one evaluation question produced in a run. The retrieved sources are
/// recorded alongside the verdict so a regression can be diagnosed from the
/// committed result file without re-running the query.
/// </summary>
public sealed record RetrievalCaseResult
{
    /// <summary>Dataset id of the question.</summary>
    public required string Id { get; init; }

    /// <summary>Category of the question.</summary>
    public required string Category { get; init; }

    /// <summary>The question text as asked.</summary>
    public required string Question { get; init; }

    /// <summary>
    /// 1-based rank of the first retrieved chunk that satisfied an expected
    /// source, or null when none of the retrieved chunks did.
    /// </summary>
    public required int? FirstMatchRank { get; init; }

    /// <summary>Number of chunks retrieval returned.</summary>
    public required int RetrievedCount { get; init; }

    /// <summary>
    /// Populated when retrieval threw. The question still counts as a miss, so a
    /// broken dependency shows up as a recall regression rather than an aborted
    /// run, but the reason is preserved.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>The retrieved chunks, best first, in the order the retrieval service returned them.</summary>
    public required IReadOnlyList<RetrievedSourceSummary> Retrieved { get; init; }
}

/// <summary>A retrieved chunk reduced to the fields that explain a match or a miss.</summary>
public sealed record RetrievedSourceSummary
{
    /// <summary>1-based position in the result list.</summary>
    public required int Rank { get; init; }

    /// <summary>Title of the source document.</summary>
    public required string Title { get; init; }

    /// <summary>Section heading, when the chunk carries one.</summary>
    public string? Section { get; init; }

    /// <summary>Page the chunk starts on, when known.</summary>
    public int? Page { get; init; }

    /// <summary>Search relevance score, rounded for diff stability.</summary>
    public required double Score { get; init; }

    /// <summary>Semantic reranker score, when the chunk was reranked.</summary>
    public double? RerankerScore { get; init; }

    /// <summary>Whether this chunk satisfied one of the question's expected sources.</summary>
    public required bool IsExpected { get; init; }
}
