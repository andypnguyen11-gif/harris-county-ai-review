using System.Text.Json;
using HarrisCountyAI.Application.Evaluation.Judging;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The human labels are the only external anchor the judge has, so a malformed
/// or unargued label is rejected rather than quietly weakening the agreement
/// rate.
/// </summary>
public sealed class ManualReviewDatasetTests
{
    [Fact]
    public void A_Well_Formed_Dataset_Round_Trips_And_Is_Searchable_By_Id()
    {
        var dataset = ManualReviewDataset.Parse("""
            {
              "description": "example",
              "version": 1,
              "reviews": [
                {"id": "gen-penalties", "verdict": "Unacceptable", "notes": "Invents a dollar figure."},
                {"id": "gen-elevation", "verdict": "Acceptable", "notes": "Traceable throughout."}
              ]
            }
            """);

        Assert.Equal(2, dataset.Reviews.Count);
        Assert.Equal(ManualVerdict.Unacceptable, dataset.Find("gen-penalties")!.Verdict);
        Assert.Equal(ManualVerdict.Acceptable, dataset.Find("GEN-ELEVATION")!.Verdict);
        Assert.Null(dataset.Find("gen-missing"));
    }

    [Fact]
    public void A_Review_Without_Notes_Is_Rejected_Because_Its_Verdict_Cannot_Be_Argued_With()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ManualReviewDataset.Parse(
            """{"reviews":[{"id":"a","verdict":"Acceptable","notes":"  "}]}"""));

        Assert.Contains("cannot be argued with", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"reviews":[]}""", "no reviews")]
    [InlineData("""{"reviews":[{"id":" ","verdict":"Acceptable","notes":"n"}]}""", "needs the id")]
    public void An_Unusable_Dataset_Is_Rejected(string json, string expectedMessageFragment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ManualReviewDataset.Parse(json));

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_Review_Ids_Are_Rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ManualReviewDataset.Parse(
            """
            {"reviews":[
              {"id":"a","verdict":"Acceptable","notes":"n"},
              {"id":"a","verdict":"Unacceptable","notes":"n"}
            ]}
            """));

        Assert.Contains("used more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_Json_Throws_A_Json_Exception()
    {
        Assert.Throws<JsonException>(() => ManualReviewDataset.Parse("{nope"));
    }
}
