using System.Text.Json;
using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// A generation dataset that parses but scores nothing is worse than one that
/// fails to parse, so validation rejects the quiet failures too.
/// </summary>
public sealed class GenerationEvaluationDatasetTests
{
    private const string ValidDataset = """
        {
          "description": "example",
          "version": 1,
          "questions": [
            {
              "id": "gen-01",
              "category": "answerable",
              "question": "How high must the lowest floor be?",
              "expectedOutcome": "Answered",
              "expectedFacts": [
                {
                  "id": "freeboard",
                  "description": "One foot above the base flood elevation",
                  "requiredPhrases": ["one foot"],
                  "anyOfPhrases": ["base flood elevation", "bfe"]
                }
              ],
              "expectedCitationTitles": ["Floodplain Regulations"],
              "notes": "why"
            }
          ]
        }
        """;

    [Fact]
    public void A_Well_Formed_Dataset_Round_Trips_Every_Field()
    {
        var dataset = GenerationEvaluationDataset.Parse(ValidDataset);

        var question = Assert.Single(dataset.Questions);
        Assert.Equal("gen-01", question.Id);
        Assert.Equal(QuestionAnswerOutcome.Answered, question.ExpectedOutcome);
        Assert.Equal(["Floodplain Regulations"], question.ExpectedCitationTitles);
        Assert.Equal("why", question.Notes);

        var fact = Assert.Single(question.ExpectedFacts);
        Assert.Equal(["one foot"], fact.RequiredPhrases);
        Assert.Equal(["base flood elevation", "bfe"], fact.AnyOfPhrases);
    }

    [Fact]
    public void An_Out_Of_Scope_Question_Needs_No_Facts()
    {
        var dataset = GenerationEvaluationDataset.Parse(
            """
            {"questions":[{"id":"a","category":"out-of-scope","question":"q","expectedOutcome":"InsufficientEvidence"}]}
            """);

        Assert.Empty(Assert.Single(dataset.Questions).ExpectedFacts);
    }

    [Fact]
    public void Expecting_Insufficient_Evidence_And_Facts_At_The_Same_Time_Is_Rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GenerationEvaluationDataset.Parse(
            """
            {"questions":[{"id":"a","category":"c","question":"q","expectedOutcome":"InsufficientEvidence",
             "expectedFacts":[{"id":"f","description":"d","requiredPhrases":["x"]}]}]}
            """));

        Assert.Contains("also lists expected facts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Fact_With_No_Phrases_Is_Rejected_Because_It_Would_Always_Pass()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GenerationEvaluationDataset.Parse(
            """
            {"questions":[{"id":"a","category":"c","question":"q","expectedOutcome":"Answered",
             "expectedFacts":[{"id":"f","description":"d"}]}]}
            """));

        Assert.Contains("specifies no phrases", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"questions":[]}""", "no questions")]
    [InlineData(
        """{"questions":[{"id":"","category":"c","question":"q","expectedOutcome":"InsufficientEvidence"}]}""",
        "non-empty id")]
    [InlineData(
        """{"questions":[{"id":"a","category":" ","question":"q","expectedOutcome":"InsufficientEvidence"}]}""",
        "no category")]
    [InlineData(
        """{"questions":[{"id":"a","category":"c","question":"","expectedOutcome":"InsufficientEvidence"}]}""",
        "no question text")]
    public void An_Unusable_Dataset_Is_Rejected(string json, string expectedMessageFragment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GenerationEvaluationDataset.Parse(json));

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_Question_Ids_Are_Rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GenerationEvaluationDataset.Parse(
            """
            {"questions":[
              {"id":"a","category":"c","question":"q","expectedOutcome":"InsufficientEvidence"},
              {"id":"a","category":"c","question":"q","expectedOutcome":"InsufficientEvidence"}
            ]}
            """));

        Assert.Contains("used more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_Fact_Ids_Within_A_Question_Are_Rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GenerationEvaluationDataset.Parse(
            """
            {"questions":[{"id":"a","category":"c","question":"q","expectedOutcome":"Answered","expectedFacts":[
              {"id":"f","description":"d","requiredPhrases":["x"]},
              {"id":"f","description":"d","requiredPhrases":["y"]}
            ]}]}
            """));

        Assert.Contains("repeats expected fact id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_Json_Throws_A_Json_Exception()
    {
        Assert.Throws<JsonException>(() => GenerationEvaluationDataset.Parse("{oops"));
    }
}
