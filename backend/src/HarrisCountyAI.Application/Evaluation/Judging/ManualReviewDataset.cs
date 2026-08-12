using System.Text.Json;

namespace HarrisCountyAI.Application.Evaluation.Judging;

/// <summary>A human's verdict on one generated answer.</summary>
public enum ManualVerdict
{
    /// <summary>A reviewer would accept this answer as it stands.</summary>
    Acceptable,

    /// <summary>A reviewer would reject it — invented content, a wrong reading, or a refusal that should have been an answer.</summary>
    Unacceptable,
}

/// <summary>One manually reviewed example, used to check the judge against a human.</summary>
public sealed record ManualReview
{
    /// <summary>Generation dataset id of the question that was reviewed.</summary>
    public required string Id { get; init; }

    /// <summary>The reviewer's verdict.</summary>
    public required ManualVerdict Verdict { get; init; }

    /// <summary>Why, in the reviewer's words.</summary>
    public required string Notes { get; init; }
}

/// <summary>
/// Human labels for the generated answers, so the judge can be checked against
/// something other than itself.
/// </summary>
/// <remarks>
/// An automated judge is only worth trusting to the extent it agrees with a
/// person on cases a person has actually looked at. This dataset is that
/// anchor: small, hand-labeled, and compared against every judge run so a
/// prompt change that makes the judge more agreeable rather than more accurate
/// shows up as falling agreement.
/// </remarks>
public sealed record ManualReviewDataset
{
    /// <summary>How the labels were produced and by whom.</summary>
    public string? Description { get; init; }

    /// <summary>Schema version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The reviewed examples.</summary>
    public required IReadOnlyList<ManualReview> Reviews { get; init; }

    /// <summary>Parses and validates the dataset.</summary>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="InvalidOperationException">The JSON parses but is not usable.</exception>
    public static ManualReviewDataset Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var dataset = JsonSerializer.Deserialize<ManualReviewDataset>(json, EvaluationJson.ReadOptions)
            ?? throw new InvalidOperationException("The manual review dataset was empty.");

        if (dataset.Reviews is null || dataset.Reviews.Count == 0)
        {
            throw new InvalidOperationException("The manual review dataset contains no reviews.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var review in dataset.Reviews)
        {
            if (string.IsNullOrWhiteSpace(review.Id))
            {
                throw new InvalidOperationException("Every manual review needs the id of the question it reviews.");
            }

            if (!seen.Add(review.Id))
            {
                throw new InvalidOperationException($"Manual review id '{review.Id}' is used more than once.");
            }

            if (string.IsNullOrWhiteSpace(review.Notes))
            {
                throw new InvalidOperationException(
                    $"Manual review '{review.Id}' has no notes, so its verdict cannot be argued with.");
            }
        }

        return dataset;
    }

    /// <summary>The review for a question, or null when it was not reviewed.</summary>
    public ManualReview? Find(string id) =>
        Reviews.FirstOrDefault(review => string.Equals(review.Id, id, StringComparison.OrdinalIgnoreCase));
}
