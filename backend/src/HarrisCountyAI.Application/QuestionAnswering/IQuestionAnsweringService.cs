namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// Answers natural-language questions from the Harris County reference corpus:
/// retrieves relevant passages, asks the language model for an evidence-bound
/// answer, and returns the answer with citations — or an explicit
/// insufficient-evidence response when the corpus cannot support one.
/// </summary>
public interface IQuestionAnsweringService
{
    /// <summary>
    /// Answers <paramref name="request"/>. Never throws for model or parsing
    /// failures — those yield <see cref="QuestionAnswerOutcome.Failed"/>; only
    /// caller cancellation and invalid arguments propagate.
    /// </summary>
    Task<QuestionResponse> AnswerAsync(QuestionRequest request, CancellationToken cancellationToken = default);
}
