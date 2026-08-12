using HarrisCountyAI.Application.Evaluation.Generation;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// Fact coverage decides what "the answer was complete" means, so its rules —
/// all required phrases, at least one alternative — are pinned explicitly.
/// </summary>
public sealed class FactCoverageAnalyzerTests
{
    private static ExpectedFact Freeboard() => new()
    {
        Id = "freeboard",
        Description = "One foot above the base flood elevation",
        RequiredPhrases = ["one foot"],
        AnyOfPhrases = ["base flood elevation", "bfe"],
    };

    [Fact]
    public void A_Fact_Is_Covered_When_Required_And_Alternative_Phrases_Both_Appear()
    {
        var results = FactCoverageAnalyzer.Analyze(
            "The lowest floor must sit one foot above the BFE.", [Freeboard()]);

        var result = Assert.Single(results);
        Assert.True(result.IsCovered);
        Assert.Empty(result.MissingRequiredPhrases);
        Assert.False(result.MissingAnyOf);
    }

    [Fact]
    public void Any_One_Alternative_Is_Enough()
    {
        var results = FactCoverageAnalyzer.Analyze(
            "The lowest floor must sit one foot above the base flood elevation.", [Freeboard()]);

        Assert.True(Assert.Single(results).IsCovered);
    }

    [Fact]
    public void A_Missing_Required_Phrase_Fails_The_Fact_And_Is_Reported()
    {
        // The answer names the datum but omits the number — exactly the partial
        // answer this metric exists to catch.
        var results = FactCoverageAnalyzer.Analyze(
            "The lowest floor must sit above the base flood elevation.", [Freeboard()]);

        var result = Assert.Single(results);
        Assert.False(result.IsCovered);
        Assert.Equal(["one foot"], result.MissingRequiredPhrases);
        Assert.False(result.MissingAnyOf);
    }

    [Fact]
    public void Missing_Every_Alternative_Fails_The_Fact_And_Is_Reported_Separately()
    {
        var results = FactCoverageAnalyzer.Analyze("The lowest floor must sit one foot higher.", [Freeboard()]);

        var result = Assert.Single(results);
        Assert.False(result.IsCovered);
        Assert.Empty(result.MissingRequiredPhrases);
        Assert.True(result.MissingAnyOf);
    }

    [Fact]
    public void A_Fact_With_Only_Required_Phrases_Ignores_The_Alternative_Rule()
    {
        var fact = new ExpectedFact
        {
            Id = "site-plan",
            Description = "Requires a site plan",
            RequiredPhrases = ["site plan"],
        };

        Assert.True(Assert.Single(FactCoverageAnalyzer.Analyze("Include a site plan.", [fact])).IsCovered);
        Assert.False(Assert.Single(FactCoverageAnalyzer.Analyze("Include a survey.", [fact])).IsCovered);
    }

    [Fact]
    public void A_Correct_Paraphrase_Is_Accepted_Because_Facts_Are_Phrases_Not_A_Reference_Answer()
    {
        var results = FactCoverageAnalyzer.Analyze(
            "You'll need to build the lowest floor a full one foot over the base flood elevation for the site.",
            [Freeboard()]);

        Assert.True(Assert.Single(results).IsCovered);
    }

    [Fact]
    public void An_Empty_Answer_Covers_Nothing()
    {
        var results = FactCoverageAnalyzer.Analyze(string.Empty, [Freeboard()]);

        Assert.False(Assert.Single(results).IsCovered);
    }

    [Fact]
    public void A_Question_With_No_Expected_Facts_Produces_No_Results()
    {
        Assert.Empty(FactCoverageAnalyzer.Analyze("anything", []));
    }

    [Fact]
    public void The_Description_Travels_With_The_Result_So_A_Failure_Reads_On_Its_Own()
    {
        var result = Assert.Single(FactCoverageAnalyzer.Analyze("nothing relevant", [Freeboard()]));

        Assert.Equal("One foot above the base flood elevation", result.Description);
        Assert.Equal("freeboard", result.FactId);
    }
}
