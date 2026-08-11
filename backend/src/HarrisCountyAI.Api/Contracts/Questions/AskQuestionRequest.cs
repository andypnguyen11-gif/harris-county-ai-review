namespace HarrisCountyAI.Api.Contracts.Questions;

/// <summary>Request body for asking a question of the reference corpus or a case's documents.</summary>
public sealed record AskQuestionRequest
{
    /// <summary>The question, in natural language.</summary>
    public string? Question { get; init; }

    /// <summary>
    /// What to ask: <c>County</c> (default) answers from the reference
    /// corpus, <c>Case</c> answers from one case's uploaded documents and
    /// requires <see cref="CaseId"/>.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>The case whose documents the question is about, for case-scoped questions.</summary>
    public Guid? CaseId { get; init; }
}
