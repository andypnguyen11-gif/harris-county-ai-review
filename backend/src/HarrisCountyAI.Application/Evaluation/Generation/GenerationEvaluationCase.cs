using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>One labeled question in the generation evaluation dataset.</summary>
public sealed record GenerationEvaluationCase
{
    /// <summary>Stable identifier, unique within the dataset.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Question family — <c>answerable</c> for questions the corpus supports, or
    /// <c>out-of-scope</c> for questions it deliberately does not. Metrics are
    /// reported per category because the two measure different behaviour:
    /// answering well, and declining to answer.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>The question, phrased the way a reviewer would ask it.</summary>
    public required string Question { get; init; }

    /// <summary>
    /// How the pipeline should conclude. An out-of-scope question that produces
    /// a confident answer is a failure even if the answer reads well.
    /// </summary>
    public required QuestionAnswerOutcome ExpectedOutcome { get; init; }

    /// <summary>Facts a correct answer must contain. Empty for out-of-scope questions.</summary>
    public IReadOnlyList<ExpectedFact> ExpectedFacts { get; init; } = [];

    /// <summary>
    /// Document titles a correct answer should cite. Empty means citation
    /// titles are not scored for this question, only citation presence.
    /// </summary>
    public IReadOnlyList<string> ExpectedCitationTitles { get; init; } = [];

    /// <summary>Optional note explaining the expectation.</summary>
    public string? Notes { get; init; }
}
