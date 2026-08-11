using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// A question to answer from the Harris County reference corpus or, when
/// scoped to a case, from that case's uploaded documents.
/// </summary>
public sealed record QuestionRequest
{
    /// <summary>Longest accepted question, in characters.</summary>
    public const int MaxQuestionLength = 1000;

    /// <summary>The reviewer's question, in natural language.</summary>
    public required string Question { get; init; }

    /// <summary>
    /// What the question is asked of. Defaults to the county reference
    /// corpus; <see cref="QuestionScope.Case"/> requires <see cref="CaseId"/>.
    /// </summary>
    public QuestionScope Scope { get; init; } = QuestionScope.County;

    /// <summary>
    /// The case whose documents the question is about. Required when
    /// <see cref="Scope"/> is <see cref="QuestionScope.Case"/>.
    /// </summary>
    public Guid? CaseId { get; init; }

    /// <summary>Number of passages to retrieve as evidence.</summary>
    public int TopK { get; init; } = RetrievalRequest.DefaultTopK;
}
