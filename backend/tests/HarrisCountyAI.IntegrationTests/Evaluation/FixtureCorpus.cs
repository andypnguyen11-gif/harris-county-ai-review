using System.Text.Json;
using HarrisCountyAI.Application.Evaluation;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// The committed synthetic corpus the offline evaluation harness retrieves
/// from. It is not the real Harris County corpus — see the file's own
/// description — but its titles, sections, and page numbers mirror the real
/// one so that matching, page tolerance, and per-category metrics are all
/// genuinely exercised without an Azure account.
/// </summary>
public sealed record FixtureCorpus
{
    /// <summary>Relative location of the committed fixture corpus under the evaluation root.</summary>
    public static readonly string[] Location = ["fixtures", "retrieval", "fixture-corpus.json"];

    /// <summary>What the corpus is and, emphatically, what it is not.</summary>
    public string? Description { get; init; }

    /// <summary>Schema version of the fixture file.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The corpus passages.</summary>
    public required IReadOnlyList<FixturePassage> Passages { get; init; }

    /// <summary>Loads the committed fixture corpus from the repository.</summary>
    public static FixtureCorpus Load()
    {
        var json = EvaluationWorkspace.ReadText(Location);
        var corpus = JsonSerializer.Deserialize<FixtureCorpus>(json, EvaluationJson.ReadOptions)
            ?? throw new InvalidOperationException("The fixture corpus was empty.");

        if (corpus.Passages.Count == 0)
        {
            throw new InvalidOperationException("The fixture corpus contains no passages.");
        }

        return corpus;
    }
}

/// <summary>One synthetic passage in the fixture corpus.</summary>
public sealed record FixturePassage
{
    /// <summary>Stable id, unique within the corpus.</summary>
    public required string Id { get; init; }

    /// <summary>Document title, matching the titles the evaluation dataset expects.</summary>
    public required string Title { get; init; }

    /// <summary>Section heading, or null for passages that carry none.</summary>
    public string? Section { get; init; }

    /// <summary>Page the passage starts on.</summary>
    public int? Page { get; init; }

    /// <summary>The passage text.</summary>
    public required string Text { get; init; }
}
