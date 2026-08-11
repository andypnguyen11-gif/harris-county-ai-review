using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.QuestionAnswering.Prompts;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// Default <see cref="IQuestionAnsweringService"/>: retrieves evidence from
/// the requested scope (the county reference corpus, or one case's uploaded
/// documents), asks the language model for a strictly grounded JSON answer,
/// and resolves the model's source numbers back to citations. Fail-closed
/// throughout — no retrieved evidence yields an insufficient-evidence
/// response without a model call, an answer that cites no retrievable source
/// is downgraded to insufficient evidence, and model or parsing failures
/// yield <see cref="QuestionAnswerOutcome.Failed"/> rather than an
/// unverifiable answer. Case-scoped questions require a case id; the
/// retrieval layer enforces the matching case filter on every query.
/// </summary>
public sealed class QuestionAnsweringService : IQuestionAnsweringService
{
    /// <summary>Cap on model output; grounded answers are short prose plus a citation list.</summary>
    private const int MaxAnswerOutputTokens = 800;

    /// <summary>Cap on the answer text surfaced to reviewers.</summary>
    private const int MaxAnswerLength = 4000;

    /// <summary>Everything about a scope that differs between county and case questions.</summary>
    private sealed record ScopeProfile(
        string SystemPrompt,
        Func<string, IReadOnlyList<RetrievedChunk>, string> BuildUserPrompt,
        string PromptVersion,
        string ResponseSchemaName,
        string NoEvidenceMessage,
        string ModelInsufficientMessage);

    private static readonly ScopeProfile CountyProfile = new(
        GroundedQuestionPrompt.SystemPrompt,
        (question, sources) => GroundedQuestionPrompt.BuildUserPrompt(question, sources),
        GroundedQuestionPrompt.Version,
        GroundedQuestionPrompt.ResponseSchemaName,
        "No relevant Harris County reference material was found for this question.",
        "The Harris County reference corpus does not contain enough information to answer this question.");

    private static readonly ScopeProfile CaseProfile = new(
        CaseQuestionPrompt.SystemPrompt,
        (question, sources) => CaseQuestionPrompt.BuildUserPrompt(question, sources),
        CaseQuestionPrompt.Version,
        CaseQuestionPrompt.ResponseSchemaName,
        "No relevant content was found in this case's submitted documents for this question.",
        "This case's submitted documents do not contain enough information to answer this question.");

    private readonly IRetrievalService _retrievalService;
    private readonly ILanguageModelService _languageModel;
    private readonly ILogger<QuestionAnsweringService> _logger;

    public QuestionAnsweringService(
        IRetrievalService retrievalService,
        ILanguageModelService languageModel,
        ILogger<QuestionAnsweringService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(retrievalService);
        ArgumentNullException.ThrowIfNull(languageModel);

        _retrievalService = retrievalService;
        _languageModel = languageModel;
        _logger = logger ?? NullLogger<QuestionAnsweringService>.Instance;
    }

    public async Task<QuestionResponse> AnswerAsync(
        QuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("The question must not be empty.", nameof(request));
        }

        if (request.Scope == QuestionScope.Case && (request.CaseId is null || request.CaseId == Guid.Empty))
        {
            throw new ArgumentException(
                "A case-scoped question requires the id of the case it is about.", nameof(request));
        }

        var profile = request.Scope == QuestionScope.Case ? CaseProfile : CountyProfile;

        var sources = await _retrievalService.RetrieveAsync(
            new RetrievalRequest
            {
                Query = request.Question,
                TopK = request.TopK,
                Scope = request.Scope == QuestionScope.Case ? SourceType.Case : SourceType.County,
                CaseId = request.Scope == QuestionScope.Case ? request.CaseId : null,
            },
            cancellationToken);

        if (sources.Count == 0)
        {
            _logger.LogInformation(
                "{Scope} Q&A retrieved no evidence; skipping the model call.", request.Scope);
            return new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                Answer = profile.NoEvidenceMessage,
                Citations = [],
                PromptVersion = profile.PromptVersion,
            };
        }

        var modelRequest = new ModelRequest
        {
            SystemPrompt = profile.SystemPrompt,
            UserPrompt = profile.BuildUserPrompt(request.Question, sources),
            ExpectsJsonResponse = true,
            JsonResponseSchemaName = profile.ResponseSchemaName,
            MaxOutputTokens = MaxAnswerOutputTokens,
            PromptVersion = profile.PromptVersion,
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
            _logger.LogWarning(exception, "{Scope} Q&A failed to call the language model.", request.Scope);
            return Failed(
                "The language model could not be reached, so the question was not answered.",
                profile);
        }

        var result = ParseResponse(response, sources, profile);
        _logger.LogInformation(
            "{Scope} Q&A concluded {Outcome} with {CitationCount} citations from {SourceCount} sources "
            + "(deployment {Deployment}, prompt {PromptVersion}).",
            request.Scope,
            result.Outcome,
            result.Citations.Count,
            sources.Count,
            response.ModelDeployment,
            profile.PromptVersion);
        return result;
    }

    private QuestionResponse ParseResponse(
        ModelResponse response,
        IReadOnlyList<RetrievedChunk> sources,
        ScopeProfile profile)
    {
        var json = ExtractJsonObject(response.Content);
        if (json is null)
        {
            _logger.LogWarning("Q&A response contained no parsable JSON object.");
            return Failed(
                "The model response did not contain the expected JSON answer, so the question was not answered.",
                profile,
                response.ModelDeployment);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out var statusElement)
                || statusElement.ValueKind != JsonValueKind.String)
            {
                return Failed(
                    "The model response did not include a status in the expected shape, so the question was not answered.",
                    profile,
                    response.ModelDeployment);
            }

            var answer = ReadAnswer(root);
            return NormalizeStatus(statusElement.GetString()) switch
            {
                "answered" => BuildAnsweredResponse(root, answer, sources, response.ModelDeployment, profile),
                "insufficientevidence" => new QuestionResponse
                {
                    Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                    Answer = string.IsNullOrWhiteSpace(answer) ? profile.ModelInsufficientMessage : answer,
                    Citations = [],
                    PromptVersion = profile.PromptVersion,
                    ModelDeployment = response.ModelDeployment,
                },
                var status => FailedForUnknownStatus(status, profile, response.ModelDeployment),
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Q&A response was not valid JSON.");
            return Failed(
                "The model response was not valid JSON, so the question was not answered.",
                profile,
                response.ModelDeployment);
        }
    }

    private QuestionResponse BuildAnsweredResponse(
        JsonElement root,
        string answer,
        IReadOnlyList<RetrievedChunk> sources,
        string modelDeployment,
        ScopeProfile profile)
    {
        var citations = ResolveCitations(root, sources);

        if (string.IsNullOrWhiteSpace(answer) || citations.Count == 0)
        {
            // An answer that cites nothing verifiable is not a grounded answer.
            // Fail closed to insufficient evidence rather than presenting it.
            _logger.LogWarning(
                "Q&A answer had {CitationCount} resolvable citations and answer length {AnswerLength}; "
                + "downgrading to insufficient evidence.",
                citations.Count,
                answer.Length);
            return new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                Answer = profile.ModelInsufficientMessage,
                Citations = [],
                PromptVersion = profile.PromptVersion,
                ModelDeployment = modelDeployment,
            };
        }

        return new QuestionResponse
        {
            Outcome = QuestionAnswerOutcome.Answered,
            Answer = answer,
            Citations = citations,
            PromptVersion = profile.PromptVersion,
            ModelDeployment = modelDeployment,
        };
    }

    /// <summary>
    /// Maps the model's cited source numbers back to the retrieved chunks,
    /// ignoring duplicates, non-numbers, and numbers outside 1..N.
    /// </summary>
    private static IReadOnlyList<Citation> ResolveCitations(
        JsonElement root,
        IReadOnlyList<RetrievedChunk> sources)
    {
        if (!root.TryGetProperty("citations", out var citationsElement)
            || citationsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var citations = new List<Citation>();
        var seen = new HashSet<int>();
        foreach (var element in citationsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Number
                || !element.TryGetInt32(out var number)
                || number < 1
                || number > sources.Count
                || !seen.Add(number))
            {
                continue;
            }

            var source = sources[number - 1];
            citations.Add(new Citation
            {
                Number = number,
                ChunkId = source.ChunkId,
                DocumentId = source.DocumentId,
                Title = source.Title,
                Section = source.Section,
                Page = source.Page,
                SourceUrl = source.SourceUrl,
            });
        }

        return citations;
    }

    private static string ReadAnswer(JsonElement root)
    {
        var answer = root.TryGetProperty("answer", out var answerElement)
            && answerElement.ValueKind == JsonValueKind.String
                ? answerElement.GetString()!.Trim()
                : string.Empty;
        return answer.Length > MaxAnswerLength ? answer[..MaxAnswerLength] : answer;
    }

    /// <summary>Locates the JSON object in the raw content, tolerating surrounding prose or code fences.</summary>
    private static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    /// <summary>Normalizes the status string, ignoring case, spacing, and separators.</summary>
    private static string NormalizeStatus(string? status)
        => string.Concat((status ?? string.Empty).Where(char.IsLetter)).ToLowerInvariant();

    private QuestionResponse FailedForUnknownStatus(string status, ScopeProfile profile, string modelDeployment)
    {
        _logger.LogWarning("Q&A returned unrecognized status '{Status}'.", status);
        return Failed(
            "The model returned an unrecognized status, so the question was not answered.",
            profile,
            modelDeployment);
    }

    private static QuestionResponse Failed(
        string explanation,
        ScopeProfile profile,
        string? modelDeployment = null) => new()
        {
            Outcome = QuestionAnswerOutcome.Failed,
            Answer = explanation,
            Citations = [],
            PromptVersion = profile.PromptVersion,
            ModelDeployment = modelDeployment,
        };
}
