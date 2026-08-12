using System.Text.Json;
using HarrisCountyAI.Application.Evaluation.Retrieval;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// Dataset parsing fails loudly. A silently-degraded dataset would produce a
/// plausible-looking report measuring nothing, which is worse than no report.
/// </summary>
public sealed class RetrievalEvaluationDatasetTests
{
    private const string ValidDataset = """
        {
          "description": "example",
          "version": 2,
          "questions": [
            {
              "id": "section-01",
              "category": "section-number",
              "question": "What does Section 4.2 require?",
              "expectedSources": [
                { "title": "Floodplain Regulations", "section": "Section 4.2", "page": 9 }
              ],
              "notes": "why this expectation"
            }
          ]
        }
        """;

    [Fact]
    public void A_Well_Formed_Dataset_Round_Trips_Every_Field()
    {
        var dataset = RetrievalEvaluationDataset.Parse(ValidDataset);

        Assert.Equal("example", dataset.Description);
        Assert.Equal(2, dataset.Version);
        var question = Assert.Single(dataset.Questions);
        Assert.Equal("section-01", question.Id);
        Assert.Equal("section-number", question.Category);
        Assert.Equal("why this expectation", question.Notes);
        var source = Assert.Single(question.ExpectedSources);
        Assert.Equal("Floodplain Regulations", source.Title);
        Assert.Equal("Section 4.2", source.Section);
        Assert.Equal(9, source.Page);
    }

    [Fact]
    public void Section_And_Page_Are_Optional()
    {
        var dataset = RetrievalEvaluationDataset.Parse(
            """{"questions":[{"id":"a","category":"semantic","question":"q","expectedSources":[{"title":"t"}]}]}""");

        var source = Assert.Single(Assert.Single(dataset.Questions).ExpectedSources);
        Assert.Null(source.Section);
        Assert.Null(source.Page);
    }

    [Fact]
    public void Malformed_Json_Throws_A_Json_Exception()
    {
        Assert.Throws<JsonException>(() => RetrievalEvaluationDataset.Parse("{not json"));
    }

    [Theory]
    [InlineData("""{"questions":[]}""", "no questions")]
    [InlineData(
        """{"questions":[{"id":" ","category":"c","question":"q","expectedSources":[{"title":"t"}]}]}""",
        "non-empty id")]
    [InlineData(
        """{"questions":[{"id":"a","category":"","question":"q","expectedSources":[{"title":"t"}]}]}""",
        "no category")]
    [InlineData(
        """{"questions":[{"id":"a","category":"c","question":" ","expectedSources":[{"title":"t"}]}]}""",
        "no question text")]
    [InlineData(
        """{"questions":[{"id":"a","category":"c","question":"q","expectedSources":[]}]}""",
        "no expected sources")]
    [InlineData(
        """{"questions":[{"id":"a","category":"c","question":"q","expectedSources":[{"title":" "}]}]}""",
        "no title")]
    [InlineData(
        """{"questions":[{"id":"a","category":"c","question":"q","expectedSources":[{"title":"t","page":0}]}]}""",
        "page below 1")]
    public void An_Unusable_Dataset_Is_Rejected(string json, string expectedMessageFragment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RetrievalEvaluationDataset.Parse(json));

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_Ids_Are_Rejected_Because_Results_Are_Correlated_By_Id()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RetrievalEvaluationDataset.Parse(
            """
            {"questions":[
              {"id":"a","category":"c","question":"q","expectedSources":[{"title":"t"}]},
              {"id":"A","category":"c","question":"q","expectedSources":[{"title":"t"}]}
            ]}
            """));

        Assert.Contains("used more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Empty_Document_Is_Rejected()
    {
        Assert.Throws<ArgumentException>(() => RetrievalEvaluationDataset.Parse("  "));
    }
}
