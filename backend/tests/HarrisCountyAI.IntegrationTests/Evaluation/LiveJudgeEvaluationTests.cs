using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.Evaluation.Judging;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Answers the generation dataset with the live pipeline and grades the results
/// with a live judge, writing the report to
/// <c>evaluation/datasets/generation/results/judge-latest-live.json</c>.
/// </summary>
/// <remarks>
/// The most expensive run in the harness: two model completions per question —
/// one to answer, one to judge — plus an embedding call and a search query for
/// each. Skipped unless <c>RUN_EVALUATION=1</c> and the Azure settings are
/// present. Run it through
/// <c>evaluation/scripts/run-judge-evaluation.sh --live</c>.
/// </remarks>
public sealed class LiveJudgeEvaluationTests
{
    [LiveEvaluationFact(
        "Search__Endpoint",
        "Search__ApiKey",
        "Embeddings__Endpoint",
        "Embeddings__ApiKey",
        "LanguageModel__Endpoint",
        "LanguageModel__ApiKey")]
    public async Task Live_Judge_Grades_The_Generation_Dataset_And_Writes_A_Report()
    {
        var dataset = GenerationEvaluationDataset.Parse(
            EvaluationWorkspace.ReadText(GenerationEvaluationFiles.Dataset));
        var manualReviews = ManualReviewDataset.Parse(
            EvaluationWorkspace.ReadText(JudgeEvaluationFiles.ManualReviews));

        await using var provider = LiveEvaluationHost.BuildGenerationProvider();
        var languageModel = provider.GetRequiredService<ILanguageModelService>();
        var recorder = new RecordingRetrievalService(provider.GetRequiredService<IRetrievalService>());
        var questionAnswering = new QuestionAnsweringService(
            recorder, languageModel, provider.GetRequiredService<ILogger<QuestionAnsweringService>>());

        var transcripts = await GenerationTranscripts.CollectAsync(
            dataset, questionAnswering, recorder);
        Assert.NotEmpty(transcripts);

        var judge = new AnswerJudge(languageModel, provider.GetRequiredService<ILogger<AnswerJudge>>());
        var report = await new JudgeEvaluationRunner(
            judge, provider.GetRequiredService<ILogger<JudgeEvaluationRunner>>())
            .RunAsync(
                transcripts,
                manualReviews,
                new JudgeEvaluationOptions
                {
                    AcceptanceThreshold = 4,
                    RunType = EvaluationRunType.Live,
                    JudgeConfiguration = LiveEvaluationHost.DescribeGenerationConfiguration(),
                });

        EvaluationWorkspace.WriteText(
            EvaluationJson.Serialize(report), JudgeEvaluationFiles.LiveResult);

        Assert.Equal(EvaluationRunType.Live, report.RunType);
        // A run where the judge could not parse a single verdict is a broken
        // environment or a broken prompt, not a quality finding.
        Assert.True(
            report.Overall.JudgedCount > 0,
            "The live judge produced no usable verdicts; check the model deployment and the prompt contract.");
    }
}
