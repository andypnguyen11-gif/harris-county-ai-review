using System.Text;

namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// Shared text handling for generation scoring: one normalization rule and one
/// sentence-splitting rule, so fact coverage and unsupported-claim detection
/// can never disagree about what the answer said.
/// </summary>
public static class AnswerText
{
    /// <summary>
    /// Lowercases, replaces every non-alphanumeric run with a single space, and
    /// trims — so "Base Flood Elevation (BFE)" and "base flood elevation, bfe"
    /// compare equal, and a phrase search cannot be defeated by punctuation.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when <paramref name="phrase"/> occurs in <paramref name="text"/>
    /// after both are normalized, on whole-word boundaries so "bfe" does not
    /// match inside "bfebar".
    /// </summary>
    public static bool ContainsPhrase(string? text, string phrase)
    {
        var haystack = Normalize(text);
        var needle = Normalize(phrase);
        if (haystack.Length == 0 || needle.Length == 0)
        {
            return false;
        }

        // Padding both sides turns a substring search into a word-boundary
        // search without a regex.
        return $" {haystack} ".Contains($" {needle} ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Splits prose into sentences on terminal punctuation and newlines. Crude
    /// on purpose: the alternative is a sentence tokenizer with its own failure
    /// modes, and unsupported-claim detection only needs claim-sized units.
    /// </summary>
    public static IReadOnlyList<string> SplitSentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var sentences = new List<string>();
        var builder = new StringBuilder();
        foreach (var character in text)
        {
            if (character is '.' or '!' or '?' or '\n' or ';')
            {
                AddIfMeaningful(sentences, builder);
                continue;
            }

            builder.Append(character);
        }

        AddIfMeaningful(sentences, builder);
        return sentences;
    }

    /// <summary>Content words of a passage: normalized tokens of two or more characters.</summary>
    public static IReadOnlySet<string> ContentTokens(string? text) =>
        Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.Ordinal);

    private static void AddIfMeaningful(List<string> sentences, StringBuilder builder)
    {
        var candidate = builder.ToString().Trim();
        builder.Clear();

        // A fragment with fewer than three content words ("See below", "Yes")
        // carries no checkable claim; scoring it would only add noise.
        if (candidate.Length > 0 && ContentTokens(candidate).Count >= 3)
        {
            sentences.Add(candidate);
        }
    }
}
