namespace HarrisCountyAI.Application.Common.Telemetry;

/// <summary>
/// Metadata captured for every AI question-answering request so that any AI
/// response can be traced back to the model, prompt, and retrieved evidence
/// that produced it. Deliberately excludes document content: telemetry may
/// carry identifiers and scores, never the text of uploaded documents or
/// retrieved chunks.
/// </summary>
public sealed record AiRequestTelemetry
{
    /// <summary>Correlation id of the HTTP request that triggered the AI call.</summary>
    public required string RequestId { get; init; }

    /// <summary>Identifier of the user who asked the question, when known.</summary>
    public string? UserId { get; init; }

    /// <summary>Case the question was asked against, when case-scoped.</summary>
    public Guid? CaseId { get; init; }

    /// <summary>The user's question as submitted.</summary>
    public required string Question { get; init; }

    /// <summary>Name of the model deployment that served the request.</summary>
    public required string ModelDeployment { get; init; }

    /// <summary>Version identifier of the prompt template used.</summary>
    public string? PromptVersion { get; init; }

    /// <summary>Filter expression applied to the search index, if any.</summary>
    public string? SearchFilters { get; init; }

    /// <summary>Ids of the chunks retrieval returned, in ranked order.</summary>
    public IReadOnlyList<string> RetrievedChunkIds { get; init; } = [];

    /// <summary>Retrieval scores aligned with <see cref="RetrievedChunkIds"/>.</summary>
    public IReadOnlyList<double> RetrievalScores { get; init; } = [];

    /// <summary>Reranking scores aligned with <see cref="RetrievedChunkIds"/>, when reranking ran.</summary>
    public IReadOnlyList<double> RerankingScores { get; init; } = [];

    /// <summary>End-to-end latency of the AI request.</summary>
    public long LatencyMilliseconds { get; init; }

    /// <summary>Tokens consumed by the prompt, when the model reports usage.</summary>
    public int? PromptTokens { get; init; }

    /// <summary>Tokens consumed by the completion, when the model reports usage.</summary>
    public int? CompletionTokens { get; init; }

    /// <summary>Outcome of the request, e.g. "Answered", "InsufficientEvidence", "Failed".</summary>
    public required string ResponseStatus { get; init; }

    /// <summary>Error description when the request failed.</summary>
    public string? Error { get; init; }
}
