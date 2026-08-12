namespace HarrisCountyAI.Application.Evaluation.Judging;

/// <summary>
/// The five things the judge scores, from the PRD's evaluation criteria. Every
/// criterion uses the same 1–5 scale in the same direction — higher is always
/// better — so an aggregate is meaningful and a reader never has to remember
/// which way one of them points.
/// </summary>
public enum JudgeCriterion
{
    /// <summary>Is every claim traceable to the supplied evidence?</summary>
    Groundedness,

    /// <summary>Does the answer address the question that was asked?</summary>
    Relevance,

    /// <summary>Does the answer cover what the evidence supports?</summary>
    Completeness,

    /// <summary>Does the answer state the evidence correctly, without reversing or overstating it?</summary>
    Accuracy,

    /// <summary>
    /// Freedom from claims that go beyond the evidence. Scored in the same
    /// direction as the rest: 5 means nothing went beyond the evidence.
    /// </summary>
    UnsupportedClaims,
}
