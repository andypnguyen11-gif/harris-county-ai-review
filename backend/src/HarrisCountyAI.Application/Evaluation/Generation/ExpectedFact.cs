namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// One thing a correct answer has to say. Facts are expressed as phrase
/// requirements rather than as a reference answer, because there is no single
/// correct wording for "the lowest floor must be a foot above the base flood
/// elevation" and scoring against one would punish a correct paraphrase.
/// </summary>
public sealed record ExpectedFact
{
    /// <summary>Stable identifier, unique within its question.</summary>
    public required string Id { get; init; }

    /// <summary>What the fact is, in plain language, for whoever reads a failure.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Phrases that must all appear in the answer. Matched case- and
    /// punctuation-insensitively against the normalized answer text.
    /// </summary>
    public IReadOnlyList<string> RequiredPhrases { get; init; } = [];

    /// <summary>
    /// Phrases of which at least one must appear, for facts a correct answer can
    /// express several ways ("base flood elevation" or "BFE"). Empty means no
    /// alternative is required.
    /// </summary>
    public IReadOnlyList<string> AnyOfPhrases { get; init; } = [];
}
