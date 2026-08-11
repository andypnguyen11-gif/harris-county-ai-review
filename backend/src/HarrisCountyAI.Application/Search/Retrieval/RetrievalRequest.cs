namespace HarrisCountyAI.Application.Search.Retrieval;

/// <summary>
/// A request to retrieve relevant passages from the Harris County reference
/// corpus. The corpus scope is implicit: retrieval always filters to
/// knowledge-base chunks, so a request can narrow the corpus with metadata
/// filters but can never reach case-uploaded documents.
/// </summary>
public sealed record RetrievalRequest
{
    /// <summary>Default number of chunks to retrieve.</summary>
    public const int DefaultTopK = 5;

    /// <summary>Largest permitted <see cref="TopK"/> value.</summary>
    public const int MaxTopK = 50;

    /// <summary>Natural-language query to retrieve passages for.</summary>
    public required string Query { get; init; }

    /// <summary>Number of chunks to retrieve, between 1 and <see cref="MaxTopK"/>.</summary>
    public int TopK { get; init; } = DefaultTopK;

    /// <summary>Restricts results to one county department when set.</summary>
    public string? Department { get; init; }

    /// <summary>Restricts results to one permit type when set.</summary>
    public string? PermitType { get; init; }

    /// <summary>Restricts results to one document category (regulation, form, checklist, …) when set.</summary>
    public string? DocumentType { get; init; }
}
