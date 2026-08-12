using HarrisCountyAI.Application.Evaluation.Retrieval;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The matcher decides what "the retrieval was correct" means, so every rule it
/// applies is pinned here — a quiet change to matching would move every recall
/// number in the project without any retrieval code changing.
/// </summary>
public sealed class ExpectedSourceMatcherTests
{
    [Theory]
    [InlineData("Floodplain Regulations", "Floodplain Regulations")]
    [InlineData("floodplain regulations", "Floodplain Regulations")]
    [InlineData("Floodplain  Regulations ", "Floodplain Regulations")]
    [InlineData("Floodplain Regulations (2024)", "Floodplain Regulations 2024")]
    public void Titles_Match_Regardless_Of_Case_Spacing_And_Punctuation(string chunkTitle, string expectedTitle)
    {
        Assert.True(ExpectedSourceMatcher.TitlesMatch(chunkTitle, expectedTitle));
    }

    [Theory]
    [InlineData("Floodplain Regulations", "Drainage Criteria Manual")]
    [InlineData("", "Floodplain Regulations")]
    [InlineData(null, "Floodplain Regulations")]
    public void Different_Titles_Do_Not_Match(string? chunkTitle, string expectedTitle)
    {
        Assert.False(ExpectedSourceMatcher.TitlesMatch(chunkTitle, expectedTitle));
    }

    [Fact]
    public void A_Null_Expected_Section_Matches_Any_Section()
    {
        Assert.True(ExpectedSourceMatcher.SectionsMatch("Section 9.9", expectedSection: null));
        Assert.True(ExpectedSourceMatcher.SectionsMatch(null, expectedSection: null));
    }

    [Theory]
    [InlineData("Section 4.2", "Section 4.2")]
    [InlineData("SECTION 4.2.", "Section 4.2")]
    [InlineData("Section 4.2 Permit Application Requirements", "Section 4.2")]
    [InlineData("Section 4.2.1 Elevation Data", "Section 4.2")]
    public void An_Expected_Section_Is_Satisfied_By_Itself_Or_A_Subsection(
        string chunkSection,
        string expectedSection)
    {
        Assert.True(ExpectedSourceMatcher.SectionsMatch(chunkSection, expectedSection));
    }

    [Theory]
    [InlineData("Section 4.3", "Section 4.2")]
    [InlineData("Section 42", "Section 4.2")]
    [InlineData(null, "Section 4.2")]
    public void An_Expected_Section_Is_Not_Satisfied_By_A_Different_One(
        string? chunkSection,
        string expectedSection)
    {
        Assert.False(ExpectedSourceMatcher.SectionsMatch(chunkSection, expectedSection));
    }

    [Fact]
    public void Section_Prefix_Matching_Does_Not_Cross_A_Number_Boundary()
    {
        // "Section 4" must not be satisfied by "Section 42", which normalizes to
        // a string that starts with the same characters.
        Assert.False(ExpectedSourceMatcher.SectionsMatch("Section 42", "Section 4"));
        Assert.True(ExpectedSourceMatcher.SectionsMatch("Section 4.2", "Section 4"));
    }

    [Fact]
    public void A_Null_Expected_Page_Matches_Any_Page()
    {
        Assert.True(ExpectedSourceMatcher.PagesMatch(chunkPage: 91, expectedPage: null, tolerance: 0));
        Assert.True(ExpectedSourceMatcher.PagesMatch(chunkPage: null, expectedPage: null, tolerance: 0));
    }

    [Theory]
    [InlineData(17, 17, 0, true)]
    [InlineData(16, 17, 1, true)]
    [InlineData(18, 17, 1, true)]
    [InlineData(19, 17, 1, false)]
    [InlineData(16, 17, 0, false)]
    public void Page_Matching_Honours_The_Configured_Tolerance(
        int chunkPage,
        int expectedPage,
        int tolerance,
        bool expected)
    {
        Assert.Equal(expected, ExpectedSourceMatcher.PagesMatch(chunkPage, expectedPage, tolerance));
    }

    [Fact]
    public void A_Chunk_With_No_Page_Cannot_Satisfy_A_Page_Expectation()
    {
        Assert.False(ExpectedSourceMatcher.PagesMatch(chunkPage: null, expectedPage: 17, tolerance: 5));
    }

    [Fact]
    public void Matches_Requires_Every_Recorded_Field_To_Agree()
    {
        var chunk = FakeRetrievalService.Chunk(title: "Floodplain Regulations", section: "Section 4.2", page: 17);

        Assert.True(ExpectedSourceMatcher.Matches(
            chunk, new ExpectedSource { Title = "Floodplain Regulations", Section = "Section 4.2", Page = 17 }));
        Assert.False(ExpectedSourceMatcher.Matches(
            chunk, new ExpectedSource { Title = "Floodplain Regulations", Section = "Section 5.1", Page = 17 }));
        Assert.False(ExpectedSourceMatcher.Matches(
            chunk, new ExpectedSource { Title = "Drainage Manual", Section = "Section 4.2", Page = 17 }));
        Assert.False(ExpectedSourceMatcher.Matches(
            chunk, new ExpectedSource { Title = "Floodplain Regulations", Section = "Section 4.2", Page = 40 }));
    }

    [Fact]
    public void MatchesAny_Accepts_A_Chunk_Satisfying_Any_One_Alternative()
    {
        var chunk = FakeRetrievalService.Chunk(title: "MT-EZ Application Form Instructions", section: null, page: 2);

        Assert.True(ExpectedSourceMatcher.MatchesAny(
            chunk,
            [
                new ExpectedSource { Title = "Floodplain Regulations" },
                new ExpectedSource { Title = "MT-EZ Application Form Instructions" },
            ]));
        Assert.False(ExpectedSourceMatcher.MatchesAny(
            chunk, [new ExpectedSource { Title = "Floodplain Regulations" }]));
    }

    [Fact]
    public void A_Negative_Page_Tolerance_Is_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpectedSourceMatcher.Matches(
            FakeRetrievalService.Chunk(), new ExpectedSource { Title = "x" }, pageTolerance: -1));
    }
}
