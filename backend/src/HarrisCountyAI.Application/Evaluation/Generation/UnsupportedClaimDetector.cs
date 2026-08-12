using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// Flags sentences in an answer whose vocabulary is not present in the evidence
/// the model was actually given.
/// </summary>
/// <remarks>
/// This is a cheap lexical screen, not a groundedness verdict. It asks one
/// narrow question — did this sentence introduce content words that appear
/// nowhere in the retrieved passages? — and it answers it with no model call,
/// deterministically, for free. That makes it useful as a always-on tripwire
/// and as a way to spot the obvious failure (a model quietly adding a deadline,
/// a fee, or a statute the corpus never mentioned).
///
/// It is wrong in both directions and is meant to be read that way. A correct
/// paraphrase can score low, and a fabricated claim assembled entirely from
/// words that appear in the evidence scores fine. The semantic version of this
/// check is the LLM judge, which reasons about entailment instead of counting
/// tokens; this detector exists so that there is still a number when the judge
/// has not been run.
/// </remarks>
public static class UnsupportedClaimDetector
{
    /// <summary>
    /// Default share of a sentence's content words that must appear in the
    /// evidence before the sentence is treated as supported.
    /// </summary>
    public const double DefaultSupportThreshold = 0.6;

    /// <summary>
    /// Words that carry no source-specific signal — they appear in almost any
    /// prose about permits and would inflate every sentence's support score.
    /// </summary>
    private static readonly HashSet<string> IgnoredTokens = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "that", "this", "with", "from", "you", "your", "must", "shall",
        "may", "can", "will", "are", "is", "be", "been", "was", "were", "not", "any", "all",
        "which", "when", "where", "who", "what", "how", "has", "have", "had", "does", "do",
        "also", "than", "then", "there", "these", "those", "they", "their", "them", "its",
        "it", "as", "at", "by", "in", "of", "on", "or", "to", "an", "a", "if", "into", "per",
        "before", "after", "under", "over", "each", "such", "other", "more", "most", "some",
        "based", "including", "include", "includes", "required", "require", "requires",
        "requirement", "requirements", "according", "source", "sources", "provided",
    };

    /// <summary>
    /// Splits <paramref name="answer"/> into sentences and scores each against
    /// the combined evidence text.
    /// </summary>
    /// <param name="answer">The generated answer.</param>
    /// <param name="evidence">The passages the model was given.</param>
    /// <param name="supportThreshold">Share of content words that must appear in the evidence.</param>
    public static IReadOnlyList<ClaimSupportResult> Analyze(
        string? answer,
        IReadOnlyList<RetrievedChunk> evidence,
        double supportThreshold = DefaultSupportThreshold)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentOutOfRangeException.ThrowIfNegative(supportThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(supportThreshold, 1d);

        var evidenceTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in evidence)
        {
            evidenceTokens.UnionWith(AnswerText.ContentTokens(chunk.Text));
            evidenceTokens.UnionWith(AnswerText.ContentTokens(chunk.Title));
            evidenceTokens.UnionWith(AnswerText.ContentTokens(chunk.Section));
        }

        var results = new List<ClaimSupportResult>();
        foreach (var sentence in AnswerText.SplitSentences(answer))
        {
            var tokens = AnswerText.ContentTokens(sentence)
                .Where(token => !IgnoredTokens.Contains(token))
                .ToList();
            if (tokens.Count == 0)
            {
                continue;
            }

            var unsupportedTokens = tokens.Where(token => !evidenceTokens.Contains(token)).Order(StringComparer.Ordinal).ToList();
            var support = (double)(tokens.Count - unsupportedTokens.Count) / tokens.Count;

            results.Add(new ClaimSupportResult
            {
                Claim = sentence,
                SupportScore = Math.Round(support, 4, MidpointRounding.AwayFromZero),
                IsSupported = support >= supportThreshold,
                UnsupportedTokens = unsupportedTokens,
            });
        }

        return results;
    }
}

/// <summary>How well one sentence of an answer is backed by the retrieved evidence.</summary>
public sealed record ClaimSupportResult
{
    /// <summary>The sentence, as it appeared in the answer.</summary>
    public required string Claim { get; init; }

    /// <summary>Share of the sentence's content words that appear in the evidence, 0.0 to 1.0.</summary>
    public required double SupportScore { get; init; }

    /// <summary>Whether the score met the configured threshold.</summary>
    public required bool IsSupported { get; init; }

    /// <summary>The content words that appear nowhere in the evidence — the reason for a low score.</summary>
    public required IReadOnlyList<string> UnsupportedTokens { get; init; }
}
