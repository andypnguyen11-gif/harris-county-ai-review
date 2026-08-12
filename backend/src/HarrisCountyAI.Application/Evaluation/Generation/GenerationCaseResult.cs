using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// What one generation evaluation question produced. The generated answer and
/// its citations are recorded in full, because a metric that moved is useless
/// without the text that moved it.
/// </summary>
public sealed record GenerationCaseResult
{
    /// <summary>Dataset id of the question.</summary>
    public required string Id { get; init; }

    /// <summary>Category of the question.</summary>
    public required string Category { get; init; }

    /// <summary>The question as asked.</summary>
    public required string Question { get; init; }

    /// <summary>How the dataset said the pipeline should conclude.</summary>
    public required QuestionAnswerOutcome ExpectedOutcome { get; init; }

    /// <summary>How it actually concluded.</summary>
    public required QuestionAnswerOutcome ActualOutcome { get; init; }

    /// <summary>Whether the two agreed.</summary>
    public bool OutcomeMatched => ExpectedOutcome == ActualOutcome;

    /// <summary>The generated answer text, verbatim.</summary>
    public required string Answer { get; init; }

    /// <summary>The sources the answer cited.</summary>
    public required IReadOnlyList<CitationSummary> Citations { get; init; }

    /// <summary>Number of passages retrieved as evidence for the answer.</summary>
    public required int EvidenceCount { get; init; }

    /// <summary>Per-fact coverage; empty for questions that expect no answer.</summary>
    public required IReadOnlyList<FactCoverageResult> Facts { get; init; }

    /// <summary>
    /// Share of expected facts the answer stated, or null when the question
    /// lists none.
    /// </summary>
    public required double? FactCoverage { get; init; }

    /// <summary>
    /// Whether every cited document title was one the dataset expected. Null
    /// when the question records no title expectations, or when nothing was cited.
    /// </summary>
    public required bool? CitationTitlesMatched { get; init; }

    /// <summary>
    /// Sentence-level support analysis, or null when evidence was not recorded
    /// for this run.
    /// </summary>
    public IReadOnlyList<ClaimSupportResult>? Claims { get; init; }

    /// <summary>The claims that fell below the support threshold.</summary>
    public IReadOnlyList<string> UnsupportedClaims { get; init; } = [];

    /// <summary>Prompt version that produced the answer.</summary>
    public string? PromptVersion { get; init; }

    /// <summary>Model deployment that produced the answer, when one was called.</summary>
    public string? ModelDeployment { get; init; }

    /// <summary>Populated when answering threw; the case is then scored as a failure.</summary>
    public string? Error { get; init; }
}

/// <summary>A citation reduced to the fields a result file needs.</summary>
public sealed record CitationSummary
{
    /// <summary>Source number the answer cited.</summary>
    public required int Number { get; init; }

    /// <summary>Which corpus the cited passage came from.</summary>
    public required string Source { get; init; }

    /// <summary>Title of the cited document.</summary>
    public required string Title { get; init; }

    /// <summary>Section of the cited passage, when known.</summary>
    public string? Section { get; init; }

    /// <summary>Page of the cited passage, when known.</summary>
    public int? Page { get; init; }
}
