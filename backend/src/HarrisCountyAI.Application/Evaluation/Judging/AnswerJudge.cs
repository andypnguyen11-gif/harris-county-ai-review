using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Evaluation.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Evaluation.Judging;

/// <summary>
/// Default <see cref="IAnswerJudge"/>: builds the versioned judge prompt,
/// requests a schema-constrained JSON verdict, and parses it defensively.
/// </summary>
/// <remarks>
/// Fails closed throughout, following the same pattern as semantic validation
/// and grounded question answering. A model error, unparseable JSON, a missing
/// criterion, or a score outside 1–5 all yield
/// <see cref="JudgeOutcome.UnableToJudge"/> — never a partial verdict and never
/// a substituted default. That matters more for a judge than for most services:
/// a judge that quietly invents a 3 when it could not read the response would
/// pull every aggregate toward the middle and hide exactly the regressions it
/// was built to find.
/// </remarks>
public sealed class AnswerJudge : IAnswerJudge
{
    /// <summary>A verdict is one small JSON object; cap output so a runaway response cannot grow unbounded.</summary>
    private const int MaxVerdictOutputTokens = 900;

    /// <summary>Cap on each reasoning string surfaced in a report.</summary>
    private const int MaxReasoningLength = 400;

    /// <summary>Cap on the number of unsupported claims recorded per verdict.</summary>
    private const int MaxUnsupportedClaims = 20;

    /// <summary>JSON property names, in the order the criteria are reported.</summary>
    private static readonly (JudgeCriterion Criterion, string Property)[] CriterionProperties =
    [
        (JudgeCriterion.Groundedness, "groundedness"),
        (JudgeCriterion.Relevance, "relevance"),
        (JudgeCriterion.Completeness, "completeness"),
        (JudgeCriterion.Accuracy, "accuracy"),
        (JudgeCriterion.UnsupportedClaims, "unsupported_claims"),
    ];

    private readonly ILanguageModelService _languageModel;
    private readonly ILogger<AnswerJudge> _logger;

    public AnswerJudge(ILanguageModelService languageModel, ILogger<AnswerJudge>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(languageModel);

        _languageModel = languageModel;
        _logger = logger ?? NullLogger<AnswerJudge>.Instance;
    }

    public async Task<JudgeVerdict> JudgeAsync(
        JudgeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("The judged question must not be empty.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Answer))
        {
            // Nothing to score. This is a caller bug, not a bad answer, so it
            // must not be recorded as a low-quality verdict.
            throw new ArgumentException("The judged answer must not be empty.", nameof(request));
        }

        var modelRequest = new ModelRequest
        {
            SystemPrompt = JudgePrompt.SystemPrompt,
            UserPrompt = JudgePrompt.BuildUserPrompt(
                request.Question, request.Answer, request.Evidence, request.ExpectedFacts),
            ExpectsJsonResponse = true,
            JsonResponseSchemaName = JudgePrompt.ResponseSchemaName,
            MaxOutputTokens = MaxVerdictOutputTokens,
            PromptVersion = JudgePrompt.Version,
        };

        ModelResponse response;
        try
        {
            response = await _languageModel.GenerateAsync(modelRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The answer judge could not reach the language model.");
            return UnableToJudge("The judge model could not be reached, so the answer was not scored.");
        }

        var verdict = Parse(response);
        _logger.LogInformation(
            "Answer judge concluded {Outcome} with mean score {MeanScore} and {ClaimCount} unsupported claims "
            + "(deployment {Deployment}, prompt {PromptVersion}).",
            verdict.Outcome,
            verdict.MeanScore,
            verdict.UnsupportedClaims.Count,
            response.ModelDeployment,
            JudgePrompt.Version);
        return verdict;
    }

    private JudgeVerdict Parse(ModelResponse response)
    {
        var json = ExtractJsonObject(response.Content);
        if (json is null)
        {
            _logger.LogWarning("The judge response contained no parsable JSON object.");
            return UnableToJudge(
                "The judge response did not contain the expected JSON verdict.", response.ModelDeployment);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("scores", out var scoresElement)
                || scoresElement.ValueKind != JsonValueKind.Object)
            {
                return UnableToJudge(
                    "The judge response did not include scores in the expected shape.",
                    response.ModelDeployment);
            }

            var reasoning = root.TryGetProperty("reasoning", out var reasoningElement)
                && reasoningElement.ValueKind == JsonValueKind.Object
                    ? reasoningElement
                    : (JsonElement?)null;

            var scores = new List<JudgeCriterionScore>(CriterionProperties.Length);
            foreach (var (criterion, property) in CriterionProperties)
            {
                if (!scoresElement.TryGetProperty(property, out var scoreElement)
                    || !TryReadScore(scoreElement, out var score))
                {
                    // A partial verdict is worse than none: it would silently
                    // change what an aggregate means between runs.
                    _logger.LogWarning(
                        "The judge response was missing or malformed for criterion '{Criterion}'.", property);
                    return UnableToJudge(
                        $"The judge response did not score '{property}' on the 1-{JudgePrompt.MaxScore} scale.",
                        response.ModelDeployment);
                }

                scores.Add(new JudgeCriterionScore
                {
                    Criterion = criterion,
                    Score = score,
                    Reasoning = ReadReasoning(reasoning, property),
                });
            }

            return new JudgeVerdict
            {
                Outcome = JudgeOutcome.Judged,
                Scores = scores,
                UnsupportedClaims = ReadUnsupportedClaims(root),
                Summary = ReadText(root, "summary", "The judge provided no summary.", MaxReasoningLength),
                PromptVersion = JudgePrompt.Version,
                ModelDeployment = response.ModelDeployment,
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The judge response was not valid JSON.");
            return UnableToJudge("The judge response was not valid JSON.", response.ModelDeployment);
        }
    }

    /// <summary>Accepts an integer or a numeric string, and only inside the declared scale.</summary>
    private static bool TryReadScore(JsonElement element, out int score)
    {
        score = 0;
        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(element.GetString(), out var number) => number,
            _ => (int?)null,
        };

        if (parsed is null or < JudgePrompt.MinScore or > JudgePrompt.MaxScore)
        {
            return false;
        }

        score = parsed.Value;
        return true;
    }

    private static string ReadReasoning(JsonElement? reasoning, string property)
    {
        if (reasoning is null
            || !reasoning.Value.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            return "The judge gave no reason.";
        }

        var text = element.GetString()!.Trim();
        return text.Length > MaxReasoningLength ? text[..MaxReasoningLength] : text;
    }

    private static IReadOnlyList<string> ReadUnsupportedClaims(JsonElement root)
    {
        if (!root.TryGetProperty("unsupported_claims", out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => Truncate(item.GetString()!.Trim(), MaxReasoningLength))
            .Take(MaxUnsupportedClaims)];
    }

    private static string ReadText(JsonElement root, string property, string fallback, int maxLength)
    {
        if (!root.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            return fallback;
        }

        return Truncate(element.GetString()!.Trim(), maxLength);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] : text;

    /// <summary>Locates the JSON object in the raw content, tolerating surrounding prose or code fences.</summary>
    private static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private static JudgeVerdict UnableToJudge(string reason, string? modelDeployment = null) => new()
    {
        Outcome = JudgeOutcome.UnableToJudge,
        Scores = [],
        UnsupportedClaims = [],
        Summary = reason,
        PromptVersion = JudgePrompt.Version,
        ModelDeployment = modelDeployment,
    };
}
