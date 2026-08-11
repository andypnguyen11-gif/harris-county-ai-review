namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// The result of comparing what an applicant submitted against what Harris
/// County requires. Same three-state, fail-closed contract as
/// <see cref="QuestionResponse"/> — an <see cref="QuestionAnswerOutcome.Answered"/>
/// response always carries citations, and missing evidence is reported rather
/// than papered over — plus per-corpus evidence counts so a reviewer can see
/// which side of the comparison the evidence came from.
/// </summary>
public sealed record DualSourceQuestionResponse
{
    /// <summary>How the attempt concluded.</summary>
    public required QuestionAnswerOutcome Outcome { get; init; }

    /// <summary>
    /// The grounded comparison when <see cref="Outcome"/> is
    /// <see cref="QuestionAnswerOutcome.Answered"/>; otherwise a short
    /// explanation of why no comparison is available.
    /// </summary>
    public required string Answer { get; init; }

    /// <summary>
    /// The sources the comparison cites, each tagged with the corpus it came
    /// from. Empty unless answered.
    /// </summary>
    public required IReadOnlyList<Citation> Citations { get; init; }

    /// <summary>Number of county reference passages retrieved as evidence.</summary>
    public required int CountyEvidenceCount { get; init; }

    /// <summary>Number of case document passages retrieved as evidence.</summary>
    public required int CaseEvidenceCount { get; init; }

    /// <summary>Version of the comparison prompt that produced this response.</summary>
    public required string PromptVersion { get; init; }

    /// <summary>The model deployment that generated the comparison, when one was called.</summary>
    public string? ModelDeployment { get; init; }
}
