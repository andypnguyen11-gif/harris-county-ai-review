using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Evaluation;
using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.Evaluation.Prompts;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// An offline <see cref="ILanguageModelService"/> that replays hand-written
/// judge verdicts from <c>evaluation/fixtures/judging/scripted-verdicts.json</c>.
/// </summary>
/// <remarks>
/// It is not a judge and does not pretend to be one. It emits the exact JSON
/// shape <see cref="Application.Evaluation.Judging.AnswerJudge"/> expects, so
/// the prompt, the response contract, the defensive parser, the acceptance
/// threshold, and the manual-agreement metric all run end to end for free and
/// deterministically. A real judge run is the most expensive thing in the
/// harness; this is what makes the plumbing testable without paying for it.
/// </remarks>
public sealed class ScriptedJudgeLanguageModel : ILanguageModelService
{
    /// <summary>Relative location of the committed script under the evaluation root.</summary>
    public static readonly string[] Location = ["fixtures", "judging", "scripted-verdicts.json"];

    private readonly IReadOnlyDictionary<string, ScriptedVerdict> _byQuestion;

    private ScriptedJudgeLanguageModel(IReadOnlyDictionary<string, ScriptedVerdict> byQuestion) =>
        _byQuestion = byQuestion;

    /// <summary>Number of verdicts this service has produced.</summary>
    public int CallCount { get; private set; }

    /// <summary>
    /// Loads the committed verdicts and binds them to <paramref name="dataset"/>
    /// by question id, so a verdict with no question — or a question with no
    /// verdict — is an error rather than a silent gap in the baseline.
    /// </summary>
    public static ScriptedJudgeLanguageModel BindTo(GenerationEvaluationDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var script = JsonSerializer.Deserialize<ScriptedVerdictScript>(
            EvaluationWorkspace.ReadText(Location), EvaluationJson.ReadOptions)
            ?? throw new InvalidOperationException("The scripted judge fixture was empty.");

        var byId = script.Verdicts.ToDictionary(verdict => verdict.Id, StringComparer.Ordinal);
        var missing = dataset.Questions
            .Where(question => !byId.ContainsKey(question.Id))
            .Select(question => question.Id)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"No scripted judge verdict for question(s): {string.Join(", ", missing)}.");
        }

        var datasetIds = dataset.Questions.Select(question => question.Id).ToHashSet(StringComparer.Ordinal);
        var orphaned = byId.Keys.Where(id => !datasetIds.Contains(id)).Order(StringComparer.Ordinal).ToList();
        if (orphaned.Count > 0)
        {
            throw new InvalidOperationException(
                $"Scripted judge verdict(s) with no dataset question: {string.Join(", ", orphaned)}.");
        }

        return new ScriptedJudgeLanguageModel(dataset.Questions.ToDictionary(
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
            throw new InvalidOperationException($"No scripted judge verdict is registered for '{question}'.");
        }

        var content = JsonSerializer.Serialize(
            new
            {
                scores = new
                {
                    groundedness = scripted.Scores.Groundedness,
                    relevance = scripted.Scores.Relevance,
                    completeness = scripted.Scores.Completeness,
                    accuracy = scripted.Scores.Accuracy,
                    unsupported_claims = scripted.Scores.UnsupportedClaims,
                },
                reasoning = new
                {
                    groundedness = scripted.Reasoning?.Groundedness,
                    relevance = scripted.Reasoning?.Relevance,
                    completeness = scripted.Reasoning?.Completeness,
                    accuracy = scripted.Reasoning?.Accuracy,
                    unsupported_claims = scripted.Reasoning?.UnsupportedClaims,
                },
                unsupported_claims = scripted.UnsupportedClaims,
                summary = scripted.Summary,
            },
            EvaluationJson.WriteOptions);

        return Task.FromResult(new ModelResponse
        {
            Content = content,
            FinishReason = "Stop",
            Usage = ModelUsage.Empty,
            ModelDeployment = "offline-scripted-judge",
            Elapsed = TimeSpan.Zero,
        });
    }

    private static string ExtractQuestion(string userPrompt)
    {
        var start = userPrompt.IndexOf(JudgePrompt.QuestionBeginDelimiter, StringComparison.Ordinal);
        var end = userPrompt.IndexOf(JudgePrompt.QuestionEndDelimiter, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("The judge prompt did not contain a delimited question.");
        }

        start += JudgePrompt.QuestionBeginDelimiter.Length;
        return userPrompt[start..end].Trim();
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The committed scripted-verdict fixture file.</summary>
public sealed record ScriptedVerdictScript
{
    /// <summary>What the fixture is and, emphatically, what it is not.</summary>
    public string? Description { get; init; }

    /// <summary>Schema version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The scripted verdicts, keyed to generation dataset question ids.</summary>
    public IReadOnlyList<ScriptedVerdict> Verdicts { get; init; } = [];
}

/// <summary>One hand-written judge verdict.</summary>
public sealed record ScriptedVerdict
{
    /// <summary>Generation dataset id of the question judged.</summary>
    public required string Id { get; init; }

    /// <summary>The five criterion scores.</summary>
    public required ScriptedScores Scores { get; init; }

    /// <summary>Optional per-criterion reasons; omitted criteria report no reason.</summary>
    public ScriptedReasoning? Reasoning { get; init; }

    /// <summary>Claims the scripted judge treats as unsupported.</summary>
    public IReadOnlyList<string> UnsupportedClaims { get; init; } = [];

    /// <summary>The scripted judge's overall summary.</summary>
    public required string Summary { get; init; }
}

/// <summary>The five criterion scores, 1–5, higher is better.</summary>
public sealed record ScriptedScores
{
    public required int Groundedness { get; init; }

    public required int Relevance { get; init; }

    public required int Completeness { get; init; }

    public required int Accuracy { get; init; }

    public required int UnsupportedClaims { get; init; }
}

/// <summary>Optional per-criterion reasons.</summary>
public sealed record ScriptedReasoning
{
    public string? Groundedness { get; init; }

    public string? Relevance { get; init; }

    public string? Completeness { get; init; }

    public string? Accuracy { get; init; }

    public string? UnsupportedClaims { get; init; }
}
