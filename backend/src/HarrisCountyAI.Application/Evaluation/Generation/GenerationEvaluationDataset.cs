using System.Text.Json;
using HarrisCountyAI.Application.QuestionAnswering;

namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// The committed set of generation evaluation questions: what to ask, how the
/// pipeline should conclude, and what a correct answer has to say and cite.
/// </summary>
public sealed record GenerationEvaluationDataset
{
    /// <summary>Human-readable description of the dataset and its scoring.</summary>
    public string? Description { get; init; }

    /// <summary>Schema version; bumped when the case shape changes.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The evaluation questions.</summary>
    public required IReadOnlyList<GenerationEvaluationCase> Questions { get; init; }

    /// <summary>Parses and validates a dataset from JSON.</summary>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="InvalidOperationException">The JSON parses but is not a usable dataset.</exception>
    public static GenerationEvaluationDataset Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var dataset = JsonSerializer.Deserialize<GenerationEvaluationDataset>(json, EvaluationJson.ReadOptions)
            ?? throw new InvalidOperationException("The generation evaluation dataset was empty.");

        dataset.Validate();
        return dataset;
    }

    /// <summary>Throws when the dataset is not usable for a run.</summary>
    /// <exception cref="InvalidOperationException">The dataset is empty or internally inconsistent.</exception>
    public void Validate()
    {
        if (Questions is null || Questions.Count == 0)
        {
            throw new InvalidOperationException("The generation evaluation dataset contains no questions.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Id))
            {
                throw new InvalidOperationException("Every generation evaluation question needs a non-empty id.");
            }

            if (!seen.Add(question.Id))
            {
                throw new InvalidOperationException(
                    $"Generation evaluation question id '{question.Id}' is used more than once.");
            }

            if (string.IsNullOrWhiteSpace(question.Category))
            {
                throw new InvalidOperationException(
                    $"Generation evaluation question '{question.Id}' has no category.");
            }

            if (string.IsNullOrWhiteSpace(question.Question))
            {
                throw new InvalidOperationException(
                    $"Generation evaluation question '{question.Id}' has no question text.");
            }

            ValidateExpectations(question);
        }
    }

    private static void ValidateExpectations(GenerationEvaluationCase question)
    {
        if (question.ExpectedOutcome == QuestionAnswerOutcome.Failed)
        {
            // Failed is a technical outcome (unreachable model, unparseable
            // response). Expecting it would encode a bug as correct behaviour.
            throw new InvalidOperationException(
                $"Generation evaluation question '{question.Id}' expects Failed, which is never a correct outcome.");
        }

        if (question.ExpectedOutcome == QuestionAnswerOutcome.InsufficientEvidence
            && question.ExpectedFacts.Count > 0)
        {
            throw new InvalidOperationException(
                $"Generation evaluation question '{question.Id}' expects insufficient evidence "
                + "but also lists expected facts.");
        }

        if (question.ExpectedOutcome == QuestionAnswerOutcome.Answered && question.ExpectedFacts.Count == 0)
        {
            throw new InvalidOperationException(
                $"Generation evaluation question '{question.Id}' expects an answer but lists no expected facts, "
                + "so nothing about the answer would be scored.");
        }

        var factIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in question.ExpectedFacts)
        {
            if (string.IsNullOrWhiteSpace(fact.Id))
            {
                throw new InvalidOperationException(
                    $"Generation evaluation question '{question.Id}' has an expected fact with no id.");
            }

            if (!factIds.Add(fact.Id))
            {
                throw new InvalidOperationException(
                    $"Generation evaluation question '{question.Id}' repeats expected fact id '{fact.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(fact.Description))
            {
                throw new InvalidOperationException(
                    $"Expected fact '{fact.Id}' on question '{question.Id}' has no description.");
            }

            if (fact.RequiredPhrases.Count == 0 && fact.AnyOfPhrases.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Expected fact '{fact.Id}' on question '{question.Id}' specifies no phrases, "
                    + "so it would always be considered covered.");
            }
        }
    }
}
