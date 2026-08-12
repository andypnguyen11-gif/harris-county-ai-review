using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.Evaluation.Judging;

/// <summary>One answer to be judged, together with the evidence it was built from.</summary>
public sealed record JudgeRequest
{
    /// <summary>The question that was asked.</summary>
    public required string Question { get; init; }

    /// <summary>The answer under review.</summary>
    public required string Answer { get; init; }

    /// <summary>
    /// The passages the assistant was actually given. The judge scores against
    /// these and nothing else — an answer that is true in the world but absent
    /// from the evidence is still ungrounded.
    /// </summary>
    public required IReadOnlyList<RetrievedChunk> Evidence { get; init; }

    /// <summary>
    /// Plain-language descriptions of what a complete answer should cover, from
    /// the generation dataset. Supplied to anchor the completeness score;
    /// omitted when the dataset records none.
    /// </summary>
    public IReadOnlyList<string> ExpectedFacts { get; init; } = [];
}

/// <summary>
/// Scores an answer against the evidence it was given, using a language model.
/// </summary>
/// <remarks>
/// A development-time evaluator, not a production dependency. It exists so that
/// a change to retrieval, chunking, or the answer prompt can be argued about
/// with evidence instead of impressions — the PRD is explicit that a judge
/// should prove itself as an evaluation capability first.
/// </remarks>
public interface IAnswerJudge
{
    /// <summary>
    /// Judges one answer. Fails closed: any model error or non-conforming
    /// response yields <see cref="JudgeOutcome.UnableToJudge"/> rather than an
    /// exception or a fabricated score. Only caller cancellation propagates.
    /// </summary>
    Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken cancellationToken = default);
}
