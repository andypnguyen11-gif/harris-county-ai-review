using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Runs the generation dataset through the live pipeline — real retrieval, real
/// model — and writes the report to
/// <c>evaluation/datasets/generation/results/latest-live.json</c>.
/// </summary>
/// <remarks>
/// Skipped unless <c>RUN_EVALUATION=1</c> and the Azure settings are present.
/// This is the expensive one: one embedding call, one search query, and one
/// model completion per dataset question. Run it through
/// <c>evaluation/scripts/run-generation-evaluation.sh --live</c>.
/// </remarks>
public sealed class LiveGenerationEvaluationTests
{
    [LiveEvaluationFact(
        "Search__Endpoint",
        "Search__ApiKey",
        "Embeddings__Endpoint",
        "Embeddings__ApiKey",
        "LanguageModel__Endpoint",
        "LanguageModel__ApiKey")]
    public async Task Live_Run_Answers_The_Dataset_And_Writes_A_Report()
    {
        var dataset = GenerationEvaluationDataset.Parse(
            EvaluationWorkspace.ReadText(GenerationEvaluationFiles.Dataset));

        await using var provider = LiveEvaluationHost.BuildGenerationProvider();
        var recorder = new RecordingRetrievalService(provider.GetRequiredService<IRetrievalService>());
        var questionAnswering = new QuestionAnsweringService(
            recorder,
            provider.GetRequiredService<Application.Common.AI.ILanguageModelService>(),
            provider.GetRequiredService<ILogger<QuestionAnsweringService>>());

        var report = await new GenerationEvaluationRunner(
            questionAnswering, recorder, provider.GetRequiredService<ILogger<GenerationEvaluationRunner>>())
            .RunAsync(
                dataset,
                new GenerationEvaluationOptions
                {
                    TopK = 5,
                    RunType = EvaluationRunType.Live,
                    PipelineConfiguration = LiveEvaluationHost.DescribeGenerationConfiguration(),
                });

        EvaluationWorkspace.WriteText(
            EvaluationJson.Serialize(report), GenerationEvaluationFiles.LiveResult);

        Assert.Equal(EvaluationRunType.Live, report.RunType);
        Assert.Equal(dataset.Questions.Count, report.Cases.Count);
        // A run where everything failed technically is a broken environment, not
        // a quality result; do not let it be committed as a baseline.
        Assert.DoesNotContain(
            report.Cases, result => result.ActualOutcome == QuestionAnswerOutcome.Failed);
    }
}
