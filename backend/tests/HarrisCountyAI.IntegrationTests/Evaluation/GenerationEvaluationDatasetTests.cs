using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Guards the committed generation dataset and the fixture script that stands
/// in for a model. A dataset that drifts from its fixtures produces a report
/// full of failures that say nothing about the pipeline.
/// </summary>
public sealed class GenerationEvaluationDatasetTests
{
    private readonly GenerationEvaluationDataset _dataset =
        GenerationEvaluationDataset.Parse(EvaluationWorkspace.ReadText(GenerationEvaluationFiles.Dataset));

    [Fact]
    public void Committed_Dataset_Parses_And_Covers_Both_Categories()
    {
        Assert.True(_dataset.Questions.Count >= 15, "The generation dataset shrank below 15 questions.");

        var categories = _dataset.Questions.Select(question => question.Category).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("answerable", categories);
        Assert.Contains("out-of-scope", categories);
    }

    [Fact]
    public void Out_Of_Scope_Questions_Expect_A_Refusal_And_Nothing_Else()
    {
        // Measuring the refusal path is the whole point of the category: a
        // fluent answer to a question the corpus cannot support is the failure
        // mode this product most needs to catch.
        var outOfScope = _dataset.Questions.Where(question => question.Category == "out-of-scope").ToList();

        Assert.NotEmpty(outOfScope);
        Assert.All(outOfScope, question =>
        {
            Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, question.ExpectedOutcome);
            Assert.Empty(question.ExpectedFacts);
            Assert.Empty(question.ExpectedCitationTitles);
        });
    }

    [Fact]
    public void Answerable_Questions_Record_Facts_And_Citation_Expectations()
    {
        var answerable = _dataset.Questions.Where(question => question.Category == "answerable").ToList();

        Assert.All(answerable, question =>
        {
            Assert.Equal(QuestionAnswerOutcome.Answered, question.ExpectedOutcome);
            Assert.NotEmpty(question.ExpectedFacts);
            Assert.NotEmpty(question.ExpectedCitationTitles);
        });
    }

    [Fact]
    public void Every_Expected_Citation_Title_Exists_In_The_Fixture_Corpus()
    {
        var corpusTitles = FixtureCorpus.Load().Passages
            .Select(passage => Application.Evaluation.Retrieval.ExpectedSourceMatcher.Normalize(passage.Title))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = _dataset.Questions
            .SelectMany(question => question.ExpectedCitationTitles.Select(title => (question.Id, title)))
            .Where(entry => !corpusTitles.Contains(
                Application.Evaluation.Retrieval.ExpectedSourceMatcher.Normalize(entry.title)))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void The_Scripted_Fixture_Covers_Every_Question_And_Nothing_Else()
    {
        // BindTo throws on either kind of drift; this pins that guarantee.
        var model = ScriptedAnswerLanguageModel.BindTo(_dataset);

        Assert.NotNull(model);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public void A_Question_Expecting_An_Answer_But_Listing_No_Facts_Is_Rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GenerationEvaluationDataset.Parse(
            """
            {"questions":[{"id":"a","category":"answerable","question":"q","expectedOutcome":"Answered"}]}
            """));

        Assert.Contains("lists no expected facts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Question_Expecting_The_Failed_Outcome_Is_Rejected()
    {
        // Failed means the model was unreachable or its output unparseable.
        // Encoding it as correct behaviour would bake a bug into the baseline.
        var exception = Assert.Throws<InvalidOperationException>(() => GenerationEvaluationDataset.Parse(
            """
            {"questions":[{"id":"a","category":"c","question":"q","expectedOutcome":"Failed"}]}
            """));

        Assert.Contains("never a correct outcome", exception.Message, StringComparison.Ordinal);
    }
}
