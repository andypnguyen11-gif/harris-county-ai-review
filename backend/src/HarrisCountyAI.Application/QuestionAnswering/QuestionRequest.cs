using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// A question to answer from the Harris County reference corpus.
/// </summary>
public sealed record QuestionRequest
{
    /// <summary>Longest accepted question, in characters.</summary>
    public const int MaxQuestionLength = 1000;

    /// <summary>The reviewer's question, in natural language.</summary>
    public required string Question { get; init; }

    /// <summary>Number of corpus passages to retrieve as evidence.</summary>
    public int TopK { get; init; } = RetrievalRequest.DefaultTopK;
}
