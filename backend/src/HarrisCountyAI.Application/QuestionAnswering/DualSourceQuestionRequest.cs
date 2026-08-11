using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// A question answered from both corpora at once: the Harris County reference
/// corpus and one case's uploaded documents. Always requires a case id — there
/// is no "compare against every case" form, because case evidence is only ever
/// retrieved one case at a time.
/// </summary>
public sealed record DualSourceQuestionRequest
{
    /// <summary>The reviewer's question, in natural language.</summary>
    public required string Question { get; init; }

    /// <summary>The case whose submitted documents form the submission side of the comparison.</summary>
    public required Guid CaseId { get; init; }

    /// <summary>
    /// Number of county reference passages to retrieve. Null lets the
    /// retrieval service apply its configured default.
    /// </summary>
    public int? CountyTopK { get; init; }

    /// <summary>
    /// Number of case document passages to retrieve. Null lets the retrieval
    /// service apply its configured default.
    /// </summary>
    public int? CaseTopK { get; init; }

    /// <summary>Restricts the county side to one permit type when set; never applied to case retrieval.</summary>
    public string? PermitType { get; init; }

    /// <summary>Restricts the county side to one department when set; never applied to case retrieval.</summary>
    public string? Department { get; init; }

    /// <summary>Longest accepted question, in characters.</summary>
    public const int MaxQuestionLength = QuestionRequest.MaxQuestionLength;

    /// <summary>Default number of passages retrieved per corpus.</summary>
    public const int DefaultTopKPerSource = RetrievalRequest.DefaultTopK;
}
