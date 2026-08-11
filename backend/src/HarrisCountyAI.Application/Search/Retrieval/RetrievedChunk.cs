namespace HarrisCountyAI.Application.Search.Retrieval;

/// <summary>
/// One passage retrieved from the Harris County reference corpus, carrying the
/// source metadata callers need to attribute (and later cite) the passage.
/// </summary>
public sealed record RetrievedChunk
{
    /// <summary>Unique chunk key in the search index.</summary>
    public required string ChunkId { get; init; }

    /// <summary>Identifier of the knowledge document the chunk came from.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>The passage text.</summary>
    public required string Text { get; init; }

    /// <summary>Title of the source document.</summary>
    public required string Title { get; init; }

    /// <summary>Section heading the passage was taken from, if known.</summary>
    public string? Section { get; init; }

    /// <summary>Page the passage starts on, if known.</summary>
    public int? Page { get; init; }

    /// <summary>Owning county department, if recorded.</summary>
    public string? Department { get; init; }

    /// <summary>Permit type the source document applies to, if recorded.</summary>
    public string? PermitType { get; init; }

    /// <summary>Document category (regulation, form, checklist, …), if recorded.</summary>
    public string? DocumentType { get; init; }

    /// <summary>Date the source document took effect, if known.</summary>
    public DateTimeOffset? EffectiveDate { get; init; }

    /// <summary>Public URL of the source document, if one exists.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Relevance score reported by the search service; higher is more relevant.</summary>
    public required double Score { get; init; }
}
