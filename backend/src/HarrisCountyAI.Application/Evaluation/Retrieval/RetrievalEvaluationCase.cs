namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>One labeled question in the retrieval evaluation dataset.</summary>
public sealed record RetrievalEvaluationCase
{
    /// <summary>Stable identifier, unique within the dataset; used to correlate results across runs.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Question family the case belongs to (for example <c>section-number</c>,
    /// <c>form-number</c>, <c>semantic</c>, <c>mixed</c>). Metrics are reported
    /// per category because retrieval modes trade off differently across them.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>The question, phrased the way a reviewer or applicant would ask it.</summary>
    public required string Question { get; init; }

    /// <summary>
    /// Sources that would each satisfy the question. A retrieval counts as a hit
    /// when any one of them appears in the results, so alternatives are listed
    /// rather than requiring every source.
    /// </summary>
    public required IReadOnlyList<ExpectedSource> ExpectedSources { get; init; }

    /// <summary>Optional note explaining why the expectation is what it is.</summary>
    public string? Notes { get; init; }
}
