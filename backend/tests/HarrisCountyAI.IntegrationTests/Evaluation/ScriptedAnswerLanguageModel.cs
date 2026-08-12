using System.Text.Json;
using System.Text.RegularExpressions;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.Evaluation.Retrieval;
using HarrisCountyAI.Application.QuestionAnswering.Prompts;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// An offline <see cref="ILanguageModelService"/> that replays hand-written
/// answers from <c>evaluation/fixtures/generation/scripted-answers.json</c>.
/// </summary>
/// <remarks>
/// It is not a model simulator. Its job is to let the real pipeline — prompt
/// construction, the JSON response contract, citation resolution, and the
/// fail-closed downgrade of an uncitable answer — run end to end for free and
/// deterministically, so the committed generation baseline detects a change in
/// any of those parts.
///
/// The one thing it models faithfully is citation behaviour. A scripted entry
/// names the document *titles* its answer should cite, and this service reads
/// the numbered source list out of the prompt it was actually given and cites
/// the numbers whose titles match. A scripted answer therefore cannot cite
/// evidence retrieval never surfaced: when the expected document is absent from
/// the prompt, the entry degrades to insufficient evidence, exactly as a
/// well-behaved model should.
/// </remarks>
public sealed partial class ScriptedAnswerLanguageModel : ILanguageModelService
{
    /// <summary>Relative location of the committed script under the evaluation root.</summary>
    public static readonly string[] Location = ["fixtures", "generation", "scripted-answers.json"];

    private readonly IReadOnlyDictionary<string, ScriptedAnswer> _byQuestion;

    private ScriptedAnswerLanguageModel(IReadOnlyDictionary<string, ScriptedAnswer> byQuestion) =>
        _byQuestion = byQuestion;

    /// <summary>Number of prompts this service has answered.</summary>
    public int CallCount { get; private set; }

    /// <summary>
    /// Loads the committed script and binds it to <paramref name="dataset"/> by
    /// question id, so every dataset question has exactly one scripted answer
    /// and an unmatched entry on either side is an error rather than a silent
    /// gap in the baseline.
    /// </summary>
    public static ScriptedAnswerLanguageModel BindTo(GenerationEvaluationDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var script = JsonSerializer.Deserialize<ScriptedAnswerScript>(
            EvaluationWorkspace.ReadText(Location), EvaluationJson.ReadOptions)
            ?? throw new InvalidOperationException("The scripted answer fixture was empty.");

        var byId = script.Answers.ToDictionary(answer => answer.Id, StringComparer.Ordinal);
        var unscripted = dataset.Questions
            .Where(question => !byId.ContainsKey(question.Id))
            .Select(question => question.Id)
            .ToList();
        if (unscripted.Count > 0)
        {
            throw new InvalidOperationException(
                $"No scripted answer for generation question(s): {string.Join(", ", unscripted)}.");
        }

        var datasetIds = dataset.Questions.Select(question => question.Id).ToHashSet(StringComparer.Ordinal);
        var orphaned = byId.Keys.Where(id => !datasetIds.Contains(id)).Order(StringComparer.Ordinal).ToList();
        if (orphaned.Count > 0)
        {
            throw new InvalidOperationException(
                $"Scripted answer(s) with no dataset question: {string.Join(", ", orphaned)}.");
        }

        return new ScriptedAnswerLanguageModel(dataset.Questions.ToDictionary(
            question => Collapse(question.Question),
            question => byId[question.Id],
            StringComparer.OrdinalIgnoreCase));
    }

    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;

        var question = ExtractQuestion(request.UserPrompt);
        if (!_byQuestion.TryGetValue(Collapse(question), out var scripted))
        {
            throw new InvalidOperationException($"No scripted answer is registered for '{question}'.");
        }

        var sources = ParseNumberedSources(request.UserPrompt);
        var citations = scripted.CiteTitles
            .SelectMany(title => sources
                .Where(source => ExpectedSourceMatcher.TitlesMatch(source.Title, title))
                .Select(source => source.Number))
            .Distinct()
            .Order()
            .ToList();

        var answered = scripted.Status == "answered" && citations.Count > 0;
        var content = answered
            ? JsonSerializer.Serialize(
                new { status = "answered", answer = scripted.Answer, citations },
                EvaluationJson.WriteOptions)
            : JsonSerializer.Serialize(
                new
                {
                    status = "insufficient_evidence",
                    answer = scripted.Status == "answered"
                        ? "The retrieved sources do not include the document this answer depends on."
                        : scripted.Answer,
                    citations = Array.Empty<int>(),
                },
                EvaluationJson.WriteOptions);

        return Task.FromResult(new ModelResponse
        {
            Content = content,
            FinishReason = "Stop",
            Usage = ModelUsage.Empty,
            ModelDeployment = "offline-scripted-fixture",
            Elapsed = TimeSpan.Zero,
        });
    }

    /// <summary>Pulls the question back out of the delimited prompt the pipeline built.</summary>
    private static string ExtractQuestion(string userPrompt)
    {
        var start = userPrompt.IndexOf(
            GroundedQuestionPrompt.QuestionBeginDelimiter, StringComparison.Ordinal);
        var end = userPrompt.IndexOf(
            GroundedQuestionPrompt.QuestionEndDelimiter, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("The prompt did not contain a delimited question.");
        }

        start += GroundedQuestionPrompt.QuestionBeginDelimiter.Length;
        return userPrompt[start..end].Trim();
    }

    /// <summary>Reads the numbered source headers the prompt presented to the model.</summary>
    private static IReadOnlyList<(int Number, string Title)> ParseNumberedSources(string userPrompt)
    {
        var sources = new List<(int, string)>();
        foreach (Match match in SourceHeaderPattern().Matches(userPrompt))
        {
            // The header is "[n] Title — Section (page N)"; only the part before
            // the first separator identifies the document.
            var header = match.Groups["header"].Value;
            var title = header.Split('—', StringSplitOptions.TrimEntries)[0];
            title = title.Split(" (page ", StringSplitOptions.TrimEntries)[0];
            sources.Add((int.Parse(match.Groups["number"].Value), title.Trim()));
        }

        return sources;
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex(@"^\[(?<number>\d+)\] (?<header>.+)$", RegexOptions.Multiline)]
    private static partial Regex SourceHeaderPattern();
}

/// <summary>The committed scripted-answer fixture file.</summary>
public sealed record ScriptedAnswerScript
{
    /// <summary>What the fixture is and, emphatically, what it is not.</summary>
    public string? Description { get; init; }

    /// <summary>Schema version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The scripted answers, keyed to dataset question ids.</summary>
    public IReadOnlyList<ScriptedAnswer> Answers { get; init; } = [];
}

/// <summary>One hand-written answer, keyed to a dataset question.</summary>
public sealed record ScriptedAnswer
{
    /// <summary>Dataset id of the question this answers.</summary>
    public required string Id { get; init; }

    /// <summary><c>answered</c> or <c>insufficient_evidence</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The answer text.</summary>
    public required string Answer { get; init; }

    /// <summary>Document titles the answer should cite; resolved against the prompt's numbered sources.</summary>
    public IReadOnlyList<string> CiteTitles { get; init; } = [];
}
