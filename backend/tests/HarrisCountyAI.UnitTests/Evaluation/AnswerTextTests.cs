using HarrisCountyAI.Application.Evaluation.Generation;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// One normalization rule and one sentence rule underpin both fact coverage and
/// unsupported-claim detection, so they are pinned here rather than implied by
/// the scorers that use them.
/// </summary>
public sealed class AnswerTextTests
{
    [Theory]
    [InlineData("Base Flood Elevation (BFE)", "base flood elevation bfe")]
    [InlineData("  fifty  percent. ", "fifty percent")]
    [InlineData("Section 4.2", "section 4 2")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalization_Collapses_Case_Punctuation_And_Whitespace(string? input, string expected)
    {
        Assert.Equal(expected, AnswerText.Normalize(input));
    }

    [Theory]
    [InlineData("The lowest floor must be one foot above the BFE.", "one foot", true)]
    [InlineData("The lowest floor must be one foot above the BFE.", "ONE  FOOT", true)]
    [InlineData("The lowest floor must be one foot above the BFE.", "two feet", false)]
    public void Phrase_Matching_Ignores_Case_And_Punctuation(string answer, string phrase, bool expected)
    {
        Assert.Equal(expected, AnswerText.ContainsPhrase(answer, phrase));
    }

    [Fact]
    public void Phrase_Matching_Respects_Word_Boundaries()
    {
        // Without boundaries, a three-letter acronym would match inside longer
        // words and every abbreviation fact would report as covered.
        Assert.False(AnswerText.ContainsPhrase("The bfebar rule applies.", "bfe"));
        Assert.True(AnswerText.ContainsPhrase("The BFE rule applies.", "bfe"));
    }

    [Fact]
    public void An_Empty_Phrase_Or_Answer_Never_Matches()
    {
        Assert.False(AnswerText.ContainsPhrase("anything at all", "  "));
        Assert.False(AnswerText.ContainsPhrase(null, "anything"));
    }

    [Fact]
    public void Sentences_Split_On_Terminal_Punctuation_And_Newlines()
    {
        var sentences = AnswerText.SplitSentences(
            "The permit is required first. Fill is prohibited in the floodway!\nA variance may be granted?");

        Assert.Equal(3, sentences.Count);
        Assert.Equal("The permit is required first", sentences[0]);
    }

    [Fact]
    public void Fragments_Too_Short_To_Carry_A_Claim_Are_Dropped()
    {
        // "Yes" and "See below" have no checkable content; scoring them would
        // only add noise to the unsupported-claim rate.
        var sentences = AnswerText.SplitSentences("Yes. See below. The lowest floor must be elevated one foot.");

        Assert.Single(sentences);
        Assert.StartsWith("The lowest floor", sentences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_Text_Yields_No_Sentences()
    {
        Assert.Empty(AnswerText.SplitSentences(null));
        Assert.Empty(AnswerText.SplitSentences("   "));
    }

    [Fact]
    public void Content_Tokens_Drop_Single_Characters_And_Deduplicate()
    {
        var tokens = AnswerText.ContentTokens("A permit, a permit; the permit 9 times");

        Assert.Contains("permit", tokens);
        Assert.Contains("times", tokens);
        Assert.DoesNotContain("a", tokens);
        Assert.DoesNotContain("9", tokens);
    }
}
