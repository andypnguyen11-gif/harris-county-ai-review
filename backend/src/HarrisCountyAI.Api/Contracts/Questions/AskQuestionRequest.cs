namespace HarrisCountyAI.Api.Contracts.Questions;

/// <summary>Request body for asking a question of the reference corpus.</summary>
public sealed record AskQuestionRequest
{
    /// <summary>The question, in natural language.</summary>
    public string? Question { get; init; }
}
