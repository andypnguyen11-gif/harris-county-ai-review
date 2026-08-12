using System.Text;
using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>
/// Decides whether a retrieved chunk satisfies an expected source. The rules
/// are deliberately deterministic — no model, no embedding — so a recall
/// number means the same thing in every run and a regression is attributable
/// to retrieval rather than to the scorer.
/// </summary>
public static class ExpectedSourceMatcher
{
    /// <summary>
    /// True when <paramref name="chunk"/> satisfies <paramref name="expected"/>:
    /// titles match after normalization, and — only when the expectation records
    /// them — the section and page match too.
    /// </summary>
    /// <param name="chunk">The retrieved passage.</param>
    /// <param name="expected">The expectation from the dataset.</param>
    /// <param name="pageTolerance">
    /// How many pages a chunk may start away from the expected page and still
    /// count, because chunks straddle page boundaries.
    /// </param>
    public static bool Matches(RetrievedChunk chunk, ExpectedSource expected, int pageTolerance = 1)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentOutOfRangeException.ThrowIfNegative(pageTolerance);

        return TitlesMatch(chunk.Title, expected.Title)
            && SectionsMatch(chunk.Section, expected.Section)
            && PagesMatch(chunk.Page, expected.Page, pageTolerance);
    }

    /// <summary>True when the chunk satisfies any one of the expected sources.</summary>
    public static bool MatchesAny(
        RetrievedChunk chunk,
        IReadOnlyList<ExpectedSource> expectedSources,
        int pageTolerance = 1)
    {
        ArgumentNullException.ThrowIfNull(expectedSources);

        for (var index = 0; index < expectedSources.Count; index++)
        {
            if (Matches(chunk, expectedSources[index], pageTolerance))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Titles match on their normalized form — case, punctuation, and whitespace
    /// differences are ignored so a dataset entry does not break when a document
    /// is re-uploaded with a slightly different display title.
    /// </summary>
    public static bool TitlesMatch(string? chunkTitle, string expectedTitle)
    {
        var chunk = Normalize(chunkTitle);
        var expected = Normalize(expectedTitle);
        return chunk.Length > 0 && expected.Length > 0 && chunk == expected;
    }

    /// <summary>
    /// A null expectation matches any section. Otherwise the chunk's section must
    /// equal the expectation or be a subsection of it ("Section 4.2" is satisfied
    /// by "Section 4.2 Permit Requirements" and by "Section 4.2.1"), which keeps
    /// the dataset stable as chunking granularity changes.
    /// </summary>
    public static bool SectionsMatch(string? chunkSection, string? expectedSection)
    {
        if (string.IsNullOrWhiteSpace(expectedSection))
        {
            return true;
        }

        var chunk = Normalize(chunkSection);
        var expected = Normalize(expectedSection);
        if (chunk.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        return chunk == expected || chunk.StartsWith(expected + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// A null expectation matches any page. Otherwise the chunk must start within
    /// <paramref name="tolerance"/> pages of the expectation; a chunk with no
    /// recorded page can never satisfy a page expectation.
    /// </summary>
    public static bool PagesMatch(int? chunkPage, int? expectedPage, int tolerance)
    {
        if (expectedPage is null)
        {
            return true;
        }

        return chunkPage is not null && Math.Abs(chunkPage.Value - expectedPage.Value) <= tolerance;
    }

    /// <summary>
    /// Lowercases, drops everything that is not a letter or digit, and collapses
    /// the remainder onto single spaces — so "Section 4.2" and "SECTION 4.2." are
    /// the same string and "Section 4.2" is still a prefix of "Section 4.2.1".
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
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
}
