namespace HarrisCountyAI.Application.Search.Retrieval;

/// <summary>
/// A request to retrieve relevant passages from one — and only one — of the
/// two corpora. The default <see cref="SourceType.County"/> scope filters to
/// knowledge-base chunks and can never reach case-uploaded documents; the
/// <see cref="SourceType.Case"/> scope requires a <see cref="CaseId"/> and
/// filters to that single case's uploaded documents, never the corpus and
/// never another case.
/// </summary>
public sealed record RetrievalRequest
{
    /// <summary>Default number of chunks to retrieve.</summary>
    public const int DefaultTopK = 5;

    /// <summary>Largest permitted <see cref="TopK"/> value.</summary>
    public const int MaxTopK = 50;

    /// <summary>Natural-language query to retrieve passages for.</summary>
    public required string Query { get; init; }

    /// <summary>
    /// The corpus to retrieve from. Defaults to the county reference corpus;
    /// <see cref="SourceType.Case"/> requires <see cref="CaseId"/>.
    /// </summary>
    public SourceType Scope { get; init; } = SourceType.County;

    /// <summary>
    /// The case whose uploaded documents to search. Required when
    /// <see cref="Scope"/> is <see cref="SourceType.Case"/>; ignored for
    /// county retrieval, whose chunks carry no case id.
    /// </summary>
    public Guid? CaseId { get; init; }

    /// <summary>
    /// Number of chunks to retrieve, between 1 and <see cref="MaxTopK"/>.
    /// Null lets the retrieval service apply its configured default
    /// (<see cref="DefaultTopK"/> unless configuration overrides it).
    /// </summary>
    public int? TopK { get; init; }

    /// <summary>Restricts results to one county department when set.</summary>
    public string? Department { get; init; }

    /// <summary>Restricts results to one permit type when set.</summary>
    public string? PermitType { get; init; }

    /// <summary>Restricts results to one document category (regulation, form, checklist, …) when set.</summary>
    public string? DocumentType { get; init; }
}
