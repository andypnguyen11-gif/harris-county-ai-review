namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>
/// A passage from the Harris County reference corpus that supports a
/// requirement — the "what the county requires" side of a comparison, so a
/// reviewer can read the county's own words rather than take the requirement
/// catalog's word for it.
/// </summary>
/// <remarks>
/// Always sourced from a county-scoped retrieval. Case documents can never
/// appear here; what the applicant submitted is carried separately by
/// <see cref="SubmissionEvidence"/>.
/// </remarks>
public sealed record RequirementEvidence
{
    /// <summary>Search-index key of the supporting chunk.</summary>
    public required string ChunkId { get; init; }

    /// <summary>Identifier of the knowledge document the passage came from.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Title of the county document.</summary>
    public required string Title { get; init; }

    /// <summary>Section heading the passage was taken from, if known.</summary>
    public string? Section { get; init; }

    /// <summary>Page the passage starts on, if known.</summary>
    public int? Page { get; init; }

    /// <summary>Public URL of the county document, if one exists.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>The supporting text, trimmed for display.</summary>
    public required string Excerpt { get; init; }
}
