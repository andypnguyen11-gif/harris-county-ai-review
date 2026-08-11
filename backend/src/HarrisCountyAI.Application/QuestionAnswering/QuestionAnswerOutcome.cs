namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>How a question-answering attempt concluded.</summary>
public enum QuestionAnswerOutcome
{
    /// <summary>The corpus contained enough evidence; the answer cites its sources.</summary>
    Answered,

    /// <summary>
    /// The corpus did not contain enough evidence to answer. The response
    /// explains that instead of inventing an answer.
    /// </summary>
    InsufficientEvidence,

    /// <summary>
    /// The attempt failed for a technical reason (model error, unparseable
    /// response). No answer content should be trusted or shown.
    /// </summary>
    Failed,
}
