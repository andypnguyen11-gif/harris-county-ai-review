namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// The outcome of answering a question from the reference corpus. Answers are
/// grounded: an <see cref="QuestionAnswerOutcome.Answered"/> response always
/// carries at least one citation, and when the corpus lacks evidence the
/// outcome says so explicitly rather than presenting an invented answer.
/// </summary>
public sealed record QuestionResponse
{
    /// <summary>How the attempt concluded.</summary>
    public required QuestionAnswerOutcome Outcome { get; init; }

    /// <summary>
    /// The grounded answer when <see cref="Outcome"/> is
    /// <see cref="QuestionAnswerOutcome.Answered"/>; otherwise a short
    /// explanation of why no answer is available.
    /// </summary>
    public required string Answer { get; init; }

    /// <summary>The corpus sources the answer cites; empty unless answered.</summary>
    public required IReadOnlyList<Citation> Citations { get; init; }

    /// <summary>Version of the grounded prompt that produced this response.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>The model deployment that generated the answer, when one was called.</summary>
    public string? ModelDeployment { get; init; }
}
