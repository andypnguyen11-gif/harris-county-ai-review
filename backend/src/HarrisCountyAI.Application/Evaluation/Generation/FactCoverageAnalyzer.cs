namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// Decides which of a question's expected facts an answer actually stated.
/// </summary>
/// <remarks>
/// Phrase matching, not similarity to a reference answer: there is no single
/// correct wording for a regulatory requirement, and scoring against one would
/// punish a correct paraphrase. A fact is covered when every one of its
/// required phrases appears and — when alternatives are listed — at least one
/// alternative does.
///
/// This is a recall check on the answer, and a deliberately literal one. It
/// catches an answer that omitted the number or the condition; it cannot catch
/// an answer that used the right words to say the wrong thing. That is what the
/// LLM judge is for.
/// </remarks>
public static class FactCoverageAnalyzer
{
    /// <summary>Scores every expected fact against the answer text.</summary>
    public static IReadOnlyList<FactCoverageResult> Analyze(
        string? answer,
        IReadOnlyList<ExpectedFact> expectedFacts)
    {
        ArgumentNullException.ThrowIfNull(expectedFacts);

        var results = new List<FactCoverageResult>(expectedFacts.Count);
        foreach (var fact in expectedFacts)
        {
            var missingRequired = fact.RequiredPhrases
                .Where(phrase => !AnswerText.ContainsPhrase(answer, phrase))
                .ToList();
            var hasAlternative = fact.AnyOfPhrases.Count == 0
                || fact.AnyOfPhrases.Any(phrase => AnswerText.ContainsPhrase(answer, phrase));

            results.Add(new FactCoverageResult
            {
                FactId = fact.Id,
                Description = fact.Description,
                IsCovered = missingRequired.Count == 0 && hasAlternative,
                MissingRequiredPhrases = missingRequired,
                MissingAnyOf = !hasAlternative,
            });
        }

        return results;
    }
}

/// <summary>Whether one expected fact appeared in the answer, and what was missing if not.</summary>
public sealed record FactCoverageResult
{
    /// <summary>Id of the expected fact.</summary>
    public required string FactId { get; init; }

    /// <summary>Plain-language description of the fact, repeated so a result file reads on its own.</summary>
    public required string Description { get; init; }

    /// <summary>Whether the answer stated the fact.</summary>
    public required bool IsCovered { get; init; }

    /// <summary>Required phrases the answer did not contain.</summary>
    public required IReadOnlyList<string> MissingRequiredPhrases { get; init; }

    /// <summary>True when the fact listed alternatives and the answer contained none of them.</summary>
    public required bool MissingAnyOf { get; init; }
}
