using System.Text.Json;

namespace HarrisCountyAI.Application.Evaluation.Retrieval;

/// <summary>
/// The committed set of retrieval evaluation questions and their expected
/// sources. Deliberately small and hand-curated: it exists to catch relative
/// regressions between retrieval configurations, not to benchmark absolute
/// quality.
/// </summary>
public sealed record RetrievalEvaluationDataset
{
    /// <summary>Human-readable description of what the dataset covers and how it is scored.</summary>
    public string? Description { get; init; }

    /// <summary>Schema version; bumped when the question or expectation shape changes.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The evaluation questions.</summary>
    public required IReadOnlyList<RetrievalEvaluationCase> Questions { get; init; }

    /// <summary>
    /// Parses a dataset from JSON and validates it: at least one question,
    /// unique non-empty ids, a category and question on every case, and at
    /// least one expected source with a title.
    /// </summary>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="InvalidOperationException">The JSON parses but is not a usable dataset.</exception>
    public static RetrievalEvaluationDataset Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var dataset = JsonSerializer.Deserialize<RetrievalEvaluationDataset>(json, EvaluationJson.ReadOptions)
            ?? throw new InvalidOperationException("The retrieval evaluation dataset was empty.");

        dataset.Validate();
        return dataset;
    }

    /// <summary>Throws when the dataset is not usable for a run.</summary>
    /// <exception cref="InvalidOperationException">The dataset is empty or internally inconsistent.</exception>
    public void Validate()
    {
        if (Questions is null || Questions.Count == 0)
        {
            throw new InvalidOperationException("The retrieval evaluation dataset contains no questions.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Id))
            {
                throw new InvalidOperationException("Every retrieval evaluation question needs a non-empty id.");
            }

            if (!seen.Add(question.Id))
            {
                throw new InvalidOperationException(
                    $"Retrieval evaluation question id '{question.Id}' is used more than once.");
            }

            if (string.IsNullOrWhiteSpace(question.Category))
            {
                throw new InvalidOperationException(
                    $"Retrieval evaluation question '{question.Id}' has no category.");
            }

            if (string.IsNullOrWhiteSpace(question.Question))
            {
                throw new InvalidOperationException(
                    $"Retrieval evaluation question '{question.Id}' has no question text.");
            }

            if (question.ExpectedSources is null || question.ExpectedSources.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Retrieval evaluation question '{question.Id}' lists no expected sources.");
            }

            foreach (var source in question.ExpectedSources)
            {
                if (string.IsNullOrWhiteSpace(source.Title))
                {
                    throw new InvalidOperationException(
                        $"Retrieval evaluation question '{question.Id}' has an expected source with no title.");
                }

                if (source.Page is <= 0)
                {
                    throw new InvalidOperationException(
                        $"Retrieval evaluation question '{question.Id}' has an expected page below 1.");
                }
            }
        }
    }
}
