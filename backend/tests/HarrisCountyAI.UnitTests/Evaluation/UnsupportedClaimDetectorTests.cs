using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The detector is a lexical screen, not a groundedness verdict. These tests
/// pin what it does catch, and — just as importantly — document what it does
/// not, so nobody mistakes a clean run for a proof of groundedness.
/// </summary>
public sealed class UnsupportedClaimDetectorTests
{
    private static IReadOnlyList<RetrievedChunk> Evidence(params string[] passages) =>
        [.. passages.Select((text, index) => FakeRetrievalService.Chunk(
            chunkId: $"chunk-{index}", text: text, title: "Floodplain Regulations", section: null, page: null))];

    private static readonly string[] Regulation =
    [
        "New construction and substantial improvement of any residential structure shall have the "
        + "lowest floor elevated to at least one foot above the base flood elevation.",
    ];

    [Fact]
    public void A_Sentence_Drawn_From_The_Evidence_Is_Supported()
    {
        var claims = UnsupportedClaimDetector.Analyze(
            "The lowest floor must be elevated one foot above the base flood elevation.", Evidence(Regulation));

        var claim = Assert.Single(claims);
        Assert.True(claim.IsSupported);
        Assert.Empty(claim.UnsupportedTokens);
        Assert.Equal(1d, claim.SupportScore);
    }

    [Fact]
    public void A_Sentence_Introducing_Content_The_Evidence_Never_Mentions_Is_Flagged()
    {
        // The archetypal failure: a fluent, plausible, entirely invented detail.
        var claims = UnsupportedClaimDetector.Analyze(
            "Applicants also receive a twelve thousand dollar rebate from the drainage authority.",
            Evidence(Regulation));

        var claim = Assert.Single(claims);
        Assert.False(claim.IsSupported);
        Assert.Contains("rebate", claim.UnsupportedTokens);
    }

    [Fact]
    public void Each_Sentence_Is_Judged_On_Its_Own()
    {
        var claims = UnsupportedClaimDetector.Analyze(
            "The lowest floor must be elevated one foot above the base flood elevation. "
            + "Applicants also receive a twelve thousand dollar rebate from the drainage authority.",
            Evidence(Regulation));

        Assert.Equal(2, claims.Count);
        Assert.True(claims[0].IsSupported);
        Assert.False(claims[1].IsSupported);
    }

    [Fact]
    public void Titles_And_Sections_Count_As_Evidence_Vocabulary()
    {
        var evidence = new[]
        {
            FakeRetrievalService.Chunk(
                text: "Encroachments are prohibited.", title: "Drainage Criteria Manual", section: "Detention", page: null),
        };

        var claims = UnsupportedClaimDetector.Analyze(
            "The Drainage Criteria Manual detention rules apply.", evidence);

        Assert.True(Assert.Single(claims).IsSupported);
    }

    [Fact]
    public void The_Threshold_Is_Configurable()
    {
        const string answer = "The lowest floor must be elevated one foot above the invented datum.";

        var lenient = UnsupportedClaimDetector.Analyze(answer, Evidence(Regulation), supportThreshold: 0.5);
        var strict = UnsupportedClaimDetector.Analyze(answer, Evidence(Regulation), supportThreshold: 1.0);

        Assert.True(Assert.Single(lenient).IsSupported);
        Assert.False(Assert.Single(strict).IsSupported);
    }

    [Fact]
    public void Evidence_From_Several_Passages_Is_Pooled()
    {
        var claims = UnsupportedClaimDetector.Analyze(
            "A drainage study must accompany the elevated lowest floor plans.",
            Evidence(Regulation[0], "Site development shall be supported by a drainage study."));

        Assert.True(Assert.Single(claims).IsSupported);
    }

    [Fact]
    public void An_Answer_With_No_Evidence_At_All_Is_Entirely_Unsupported()
    {
        var claims = UnsupportedClaimDetector.Analyze(
            "The lowest floor must be elevated one foot.", []);

        Assert.False(Assert.Single(claims).IsSupported);
        Assert.Equal(0d, Assert.Single(claims).SupportScore);
    }

    [Fact]
    public void A_Fabrication_Assembled_From_Evidence_Vocabulary_Is_Not_Caught()
    {
        // Documenting a real blind spot rather than hiding it: this sentence
        // reverses the requirement using only words the evidence contains, and
        // the lexical screen passes it. Catching this needs the LLM judge.
        var claims = UnsupportedClaimDetector.Analyze(
            "The lowest floor may be one foot below the base flood elevation.", Evidence(Regulation));

        Assert.True(Assert.Single(claims).IsSupported);
    }

    [Fact]
    public void An_Empty_Answer_Produces_No_Claims()
    {
        Assert.Empty(UnsupportedClaimDetector.Analyze(string.Empty, Evidence(Regulation)));
    }

    [Fact]
    public void A_Threshold_Outside_Zero_To_One_Is_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UnsupportedClaimDetector.Analyze("text", [], supportThreshold: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UnsupportedClaimDetector.Analyze("text", [], supportThreshold: -0.1));
    }
}
