using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Validation.Semantic.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Validation.Semantic;

/// <summary>
/// Default <see cref="ISemanticValidationService"/>: builds the versioned prompt (delimited,
/// length-capped document text), requests a strict JSON verdict from the language model, and
/// parses the response defensively. Every failure path — model error, malformed JSON, unknown
/// verdict — yields <see cref="SemanticVerdict.UnableToDetermine"/> instead of an exception;
/// only caller cancellation propagates.
/// </summary>
public sealed class SemanticValidationService : ISemanticValidationService
{
    /// <summary>Verdicts are one short JSON object; cap output so a runaway response cannot grow unbounded.</summary>
    private const int MaxVerdictOutputTokens = 300;

    /// <summary>Cap on the reasoning text surfaced to reviewers.</summary>
    private const int MaxReasoningLength = 500;

    private readonly ILanguageModelService _languageModel;
    private readonly ILogger<SemanticValidationService> _logger;
    private readonly int _maxDocumentTextLength;

    public SemanticValidationService(
        ILanguageModelService languageModel,
        ILogger<SemanticValidationService>? logger = null,
        int maxDocumentTextLength = SemanticValidationPrompt.DefaultMaxDocumentTextLength)
    {
        ArgumentNullException.ThrowIfNull(languageModel);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDocumentTextLength, 1);

        _languageModel = languageModel;
        _logger = logger ?? NullLogger<SemanticValidationService>.Instance;
        _maxDocumentTextLength = maxDocumentTextLength;
    }

    public async Task<SemanticValidationResult> EvaluateAsync(
        SemanticValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modelRequest = new ModelRequest
        {
            SystemPrompt = SemanticValidationPrompt.SystemPrompt,
            UserPrompt = SemanticValidationPrompt.BuildUserPrompt(
                request.RequirementDescription,
                request.DocumentText,
                _maxDocumentTextLength),
            ExpectsJsonResponse = true,
            JsonResponseSchemaName = SemanticValidationPrompt.ResponseSchemaName,
            MaxOutputTokens = MaxVerdictOutputTokens,
            PromptVersion = SemanticValidationPrompt.Version,
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
            _logger.LogWarning(
                exception,
                "Semantic evaluation of '{Requirement}' failed to call the language model.",
                request.Requirement);
            return UnableToDetermine($"Semantic evaluation could not run: {exception.Message}");
        }

        var result = ParseResponse(response, request.Requirement);
        _logger.LogInformation(
            "Semantic evaluation of '{Requirement}' returned {Verdict} (deployment {Deployment}, prompt {PromptVersion}).",
            request.Requirement,
            result.Verdict,
            response.ModelDeployment,
            SemanticValidationPrompt.Version);
        return result;
    }

    private SemanticValidationResult ParseResponse(ModelResponse response, string requirement)
    {
        var json = ExtractJsonObject(response.Content);
        if (json is null)
        {
            _logger.LogWarning(
                "Semantic evaluation of '{Requirement}' returned no parsable JSON object.",
                requirement);
            return UnableToDetermine(
                "The model response did not contain the expected JSON verdict, so the check could not be completed.",
                response.ModelDeployment);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("verdict", out var verdictElement)
                || verdictElement.ValueKind != JsonValueKind.String)
            {
                return UnableToDetermine(
                    "The model response did not include a verdict in the expected shape, so the check could not be completed.",
                    response.ModelDeployment);
            }

            var verdict = NormalizeVerdict(verdictElement.GetString());
            if (verdict is null)
            {
                _logger.LogWarning(
                    "Semantic evaluation of '{Requirement}' returned unrecognized verdict '{Verdict}'.",
                    requirement,
                    verdictElement.GetString());
                return UnableToDetermine(
                    "The model returned an unrecognized verdict, so the check could not be completed.",
                    response.ModelDeployment);
            }

            var reasoning = document.RootElement.TryGetProperty("reasoning", out var reasoningElement)
                && reasoningElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(reasoningElement.GetString())
                    ? reasoningElement.GetString()!.Trim()
                    : "The model provided no reasoning.";
            if (reasoning.Length > MaxReasoningLength)
            {
                reasoning = reasoning[..MaxReasoningLength];
            }

            return new SemanticValidationResult
            {
                Verdict = verdict.Value,
                Reasoning = reasoning,
                PromptVersion = SemanticValidationPrompt.Version,
                ModelDeployment = response.ModelDeployment,
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Semantic evaluation of '{Requirement}' returned malformed JSON.",
                requirement);
            return UnableToDetermine(
                "The model response was not valid JSON, so the check could not be completed.",
                response.ModelDeployment);
        }
    }

    /// <summary>
    /// Locates the JSON object within the raw model content, tolerating surrounding prose or
    /// Markdown code fences. Returns null when no braces are present at all.
    /// </summary>
    private static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    /// <summary>Maps the model's verdict string to a <see cref="SemanticVerdict"/>, ignoring case, spacing, and separators.</summary>
    private static SemanticVerdict? NormalizeVerdict(string? verdict)
    {
        if (verdict is null)
        {
            return null;
        }

        return string.Concat(verdict.Where(char.IsLetter)).ToLowerInvariant() switch
        {
            "pass" => SemanticVerdict.Pass,
            "fail" => SemanticVerdict.Fail,
            "needshumanreview" => SemanticVerdict.NeedsHumanReview,
            _ => null,
        };
    }

    private static SemanticValidationResult UnableToDetermine(string reasoning, string? modelDeployment = null) => new()
    {
        Verdict = SemanticVerdict.UnableToDetermine,
        Reasoning = reasoning,
        PromptVersion = SemanticValidationPrompt.Version,
        ModelDeployment = modelDeployment,
    };
}
