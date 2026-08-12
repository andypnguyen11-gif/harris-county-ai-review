using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Assembles the real question-answering pipeline over offline parts: fixture
/// corpus retrieval wrapped in the evidence recorder, and the scripted model.
/// </summary>
/// <remarks>
/// Everything between the question and the report is production code —
/// <see cref="QuestionAnsweringService"/>, the grounded prompt, the citation
/// resolver, the fail-closed downgrade. Only the two external dependencies are
/// swapped, which is what makes the fixture baseline worth committing.
/// </remarks>
public sealed class OfflineGenerationPipeline
{
    private OfflineGenerationPipeline(
        GenerationEvaluationDataset dataset,
        GenerationEvaluationRunner runner,
        ScriptedAnswerLanguageModel model)
    {
        Dataset = dataset;
        Runner = runner;
        Model = model;
    }

    /// <summary>The committed generation dataset.</summary>
    public GenerationEvaluationDataset Dataset { get; }

    /// <summary>A runner wired to the offline pipeline.</summary>
    public GenerationEvaluationRunner Runner { get; }

    /// <summary>The scripted model, for asserting how often it was called.</summary>
    public ScriptedAnswerLanguageModel Model { get; }

    /// <summary>Builds the offline pipeline from the committed dataset, corpus, and script.</summary>
    public static OfflineGenerationPipeline Create()
    {
        var dataset = GenerationEvaluationDataset.Parse(
            EvaluationWorkspace.ReadText(GenerationEvaluationFiles.Dataset));
        var recorder = new RecordingRetrievalService(FixtureCorpusRetrievalService.FromCommittedCorpus());
        var model = ScriptedAnswerLanguageModel.BindTo(dataset);
        var questionAnswering = new QuestionAnsweringService(recorder, model);

        return new OfflineGenerationPipeline(
            dataset, new GenerationEvaluationRunner(questionAnswering, recorder), model);
    }
}
