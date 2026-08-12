using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.Evaluation.Judging;
using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Drives the question-answering pipeline once over a dataset and captures what
/// each question produced, so the judge can grade real transcripts.
/// </summary>
/// <remarks>
/// One pass, not two. A live judge run already costs a model completion per
/// answer on top of the completion that produced it; re-answering the dataset
/// just to feed the judge would double the cheaper half of that bill for no
/// gain, and would grade answers other than the ones that were measured.
/// </remarks>
public static class GenerationTranscripts
{
    /// <summary>
    /// Answers every question and returns a transcript per case, skipping only
    /// technical failures — an insufficient-evidence response is a real answer
    /// and the judge is expected to score declining well when it was right.
    /// </summary>
    public static async Task<IReadOnlyList<JudgeEvaluationInput>> CollectAsync(
        GenerationEvaluationDataset dataset,
        IQuestionAnsweringService questionAnswering,
        IEvaluationEvidenceRecorder recorder,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(questionAnswering);
        ArgumentNullException.ThrowIfNull(recorder);

        var transcripts = new List<JudgeEvaluationInput>(dataset.Questions.Count);
        foreach (var question in dataset.Questions)
        {
            recorder.Drain();

            var response = await questionAnswering.AnswerAsync(
                new QuestionRequest
                {
                    Question = question.Question,
                    Scope = QuestionScope.County,
                    TopK = topK,
                },
                cancellationToken);
            var evidence = recorder.Drain();

            if (response.Outcome == QuestionAnswerOutcome.Failed)
            {
                continue;
            }

            transcripts.Add(new JudgeEvaluationInput
            {
                Id = question.Id,
                Category = question.Category,
                Question = question.Question,
                Answer = response.Answer,
                Evidence = evidence,
                ExpectedFacts = [.. question.ExpectedFacts.Select(fact => fact.Description)],
            });
        }

        return transcripts;
    }
}
