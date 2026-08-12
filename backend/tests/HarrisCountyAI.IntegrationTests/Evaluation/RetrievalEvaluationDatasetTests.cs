using HarrisCountyAI.Application.Evaluation.Retrieval;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Guards the committed retrieval dataset itself. A dataset that no longer
/// parses, loses its category balance, or expects a document the fixture corpus
/// has never heard of would silently turn every later measurement into noise,
/// so it is checked in CI like any other contract.
/// </summary>
public sealed class RetrievalEvaluationDatasetTests
{
    /// <summary>The PRD asks for a small curated set; this is the band the harness is designed for.</summary>
    private const int MinimumQuestions = 20;

    private const int MaximumQuestions = 30;

    private readonly RetrievalEvaluationDataset _dataset =
        RetrievalEvaluationDataset.Parse(EvaluationWorkspace.ReadText(RetrievalEvaluationFiles.Dataset));

    [Fact]
    public void Committed_Dataset_Parses_And_Holds_Between_Twenty_And_Thirty_Questions()
    {
        Assert.InRange(_dataset.Questions.Count, MinimumQuestions, MaximumQuestions);
        Assert.Equal(2, _dataset.Version);
    }

    [Fact]
    public void Question_Ids_Are_Unique()
    {
        var duplicates = _dataset.Questions
            .GroupBy(question => question.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_Category_The_Rag_Design_Compares_Is_Represented()
    {
        var categories = _dataset.Questions
            .Select(question => question.Category)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("section-number", categories);
        Assert.Contains("form-number", categories);
        Assert.Contains("semantic", categories);
    }

    [Fact]
    public void Semantic_Questions_Outnumber_Any_Other_Category()
    {
        // Reviewers ask in plain language far more often than they quote a
        // section number, so the dataset should be weighted that way too.
        var counts = _dataset.Questions
            .GroupBy(question => question.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var semantic = counts["semantic"];
        Assert.All(
            counts.Where(entry => entry.Key != "semantic"),
            entry => Assert.True(
                entry.Value < semantic,
                $"Category '{entry.Key}' has {entry.Value} questions, which is not fewer than semantic's {semantic}."));
    }

    [Fact]
    public void Every_Expected_Document_Exists_In_The_Fixture_Corpus()
    {
        // The fixture corpus is what the offline baseline retrieves from; an
        // expectation naming a document it does not contain can never be met,
        // and would look like a permanent retrieval regression.
        var corpusTitles = FixtureCorpus.Load().Passages
            .Select(passage => ExpectedSourceMatcher.Normalize(passage.Title))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = _dataset.Questions
            .SelectMany(question => question.ExpectedSources.Select(source => (question.Id, source.Title)))
            .Where(entry => !corpusTitles.Contains(ExpectedSourceMatcher.Normalize(entry.Title)))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_Expected_Section_Is_Reachable_In_The_Fixture_Corpus()
    {
        var passages = FixtureCorpus.Load().Passages;

        var unreachable = _dataset.Questions
            .Where(question => question.ExpectedSources.All(source =>
                !passages.Any(passage =>
                    ExpectedSourceMatcher.TitlesMatch(passage.Title, source.Title)
                    && ExpectedSourceMatcher.SectionsMatch(passage.Section, source.Section))))
            .Select(question => question.Id)
            .ToList();

        Assert.Empty(unreachable);
    }

    [Fact]
    public void Malformed_Datasets_Are_Rejected_With_A_Useful_Message()
    {
        var duplicateIds = """
            {"version": 1, "questions": [
              {"id": "a", "category": "semantic", "question": "q", "expectedSources": [{"title": "t"}]},
              {"id": "a", "category": "semantic", "question": "q", "expectedSources": [{"title": "t"}]}
            ]}
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => RetrievalEvaluationDataset.Parse(duplicateIds));
        Assert.Contains("used more than once", exception.Message, StringComparison.Ordinal);
    }
}
