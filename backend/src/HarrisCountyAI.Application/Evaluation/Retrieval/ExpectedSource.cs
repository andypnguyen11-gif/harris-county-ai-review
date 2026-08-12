namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>
/// One corpus source a correct retrieval is expected to surface for an
/// evaluation question. <see cref="Title"/> is always required;
/// <see cref="Section"/> and <see cref="Page"/> narrow the expectation and are
/// only checked when the dataset records them, so a question can express
/// "any passage from this document" or "this exact section" as needed.
/// </summary>
public sealed record ExpectedSource
{
    /// <summary>Title of the corpus document the evidence should come from.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Section heading the evidence should come from. Null means any section of
    /// <see cref="Title"/> counts.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Page the evidence should start on. Null means any page counts. Matching
    /// allows a small tolerance because chunks straddle page boundaries — see
    /// <see cref="RetrievalEvaluationOptions.PageTolerance"/>.
    /// </summary>
    public int? Page { get; init; }
}
