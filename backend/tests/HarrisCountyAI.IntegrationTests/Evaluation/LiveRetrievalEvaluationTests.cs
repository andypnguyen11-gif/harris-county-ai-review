using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Retrieval;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Scores the committed dataset against live Azure AI Search and writes the
/// report to <c>evaluation/datasets/retrieval/results/latest-live.json</c>.
/// </summary>
/// <remarks>
/// Skipped unless <c>RUN_EVALUATION=1</c> and the Azure settings are present,
/// because it issues one embedding call and one search query per dataset
/// question against a metered subscription. Run it through
/// <c>evaluation/scripts/run-retrieval-evaluation.sh --live</c>, which loads
/// credentials from outside the repository.
/// </remarks>
public sealed class LiveRetrievalEvaluationTests
{
    [LiveEvaluationFact(
        "Search__Endpoint", "Search__ApiKey", "Embeddings__Endpoint", "Embeddings__ApiKey")]
    public async Task Live_Run_Scores_The_Dataset_And_Writes_A_Report()
    {
        var dataset = RetrievalEvaluationDataset.Parse(
            EvaluationWorkspace.ReadText(RetrievalEvaluationFiles.Dataset));

        await using var provider = LiveEvaluationHost.BuildRetrievalProvider();
        var runner = new RetrievalEvaluationRunner(
            provider.GetRequiredService<IRetrievalService>(),
            provider.GetRequiredService<ILogger<RetrievalEvaluationRunner>>());

        var report = await runner.RunAsync(
            dataset,
            new RetrievalEvaluationOptions
            {
                TopK = 5,
                RunType = EvaluationRunType.Live,
                RetrievalConfiguration = LiveEvaluationHost.DescribeRetrievalConfiguration(),
            });

        EvaluationWorkspace.WriteText(
            EvaluationJson.Serialize(report), RetrievalEvaluationFiles.LiveResult);

        Assert.Equal(EvaluationRunType.Live, report.RunType);
        Assert.Equal(dataset.Questions.Count, report.Cases.Count);
        // A live run that retrieved nothing at all means the corpus was never
        // ingested; fail loudly rather than committing a report of zeroes.
        Assert.Contains(report.Cases, result => result.RetrievedCount > 0);
    }
}
