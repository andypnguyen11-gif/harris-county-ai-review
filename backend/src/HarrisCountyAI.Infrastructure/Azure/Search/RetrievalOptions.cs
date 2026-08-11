using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Configuration for corpus retrieval, bound from the
/// <see cref="SectionName"/> configuration section. Every setting has a safe
/// default, so the section may be omitted entirely.
/// </summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    /// <summary>How queries combine keyword and vector search.</summary>
    public RetrievalMode Mode { get; set; } = RetrievalMode.Hybrid;

    /// <summary>
    /// Number of chunks to retrieve when a request does not specify
    /// <see cref="RetrievalRequest.TopK"/>. Must be between 1 and
    /// <see cref="RetrievalRequest.MaxTopK"/>.
    /// </summary>
    public int DefaultTopK { get; set; } = RetrievalRequest.DefaultTopK;
}
