using System.Diagnostics;
using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Common.Telemetry;
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
    private readonly IAiRequestTelemetryLogger? _telemetryLogger;
    private readonly IRequestContextAccessor? _requestContext;

    public QuestionAnsweringService(
        IRetrievalService retrievalService,
        ILanguageModelService languageModel,
        ILogger<QuestionAnsweringService>? logger = null,
        IAiRequestTelemetryLogger? telemetryLogger = null,
        IRequestContextAccessor? requestContext = null)
    {
        ArgumentNullException.ThrowIfNull(retrievalService);
        ArgumentNullException.ThrowIfNull(languageModel);

        _retrievalService = retrievalService;
        _languageModel = languageModel;
        _logger = logger ?? NullLogger<QuestionAnsweringService>.Instance;
        _telemetryLogger = telemetryLogger;
        _requestContext = requestContext;
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

        if (request.Scope == QuestionScope.Both)
        {
            // Both is answered by the dual-source path, which retrieves each
            // corpus separately; routing it here would silently collapse it to
            // a single scope, so refuse rather than answer half the question.
            throw new ArgumentException(
                "A question scoped to Both must be answered by IDualSourceQuestionAnsweringService.",
                nameof(request));
        }

        if (request.Scope == QuestionScope.Case && (request.CaseId is null || request.CaseId == Guid.Empty))
        {
            throw new ArgumentException(
                "A case-scoped question requires the id of the case it is about.", nameof(request));
        }

        var isCaseScoped = request.Scope == QuestionScope.Case;
        var profile = isCaseScoped ? CaseProfile : CountyProfile;
        var sourceType = isCaseScoped ? SourceType.Case : SourceType.County;

        // Latency is measured end to end, retrieval included, because that is
        // what a reviewer waiting on an answer actually experiences.
        var stopwatch = Stopwatch.StartNew();

        var sources = await _retrievalService.RetrieveAsync(
            new RetrievalRequest
            {
                Query = request.Question,
                TopK = request.TopK,
                Scope = sourceType,
                CaseId = isCaseScoped ? request.CaseId : null,
            },
            cancellationToken);

        if (sources.Count == 0)
        {
            _logger.LogInformation(
                "{Scope} Q&A retrieved no evidence; skipping the model call.", request.Scope);
            var noEvidenceResponse = new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                Answer = profile.NoEvidenceMessage,
                Citations = [],
                PromptVersion = profile.PromptVersion,
            };
            RecordTelemetry(
                request,
                profile,
                sources,
                stopwatch,
                noEvidenceResponse.Outcome.ToString(),
                modelDeployment: AiTelemetryDefaults.NoModelDeployment);
            return noEvidenceResponse;
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
            var failure = Failed(
                "The language model could not be reached, so the question was not answered.",
                profile);
            RecordTelemetry(
                request,
                profile,
                sources,
                stopwatch,
                failure.Outcome.ToString(),
                modelDeployment: AiTelemetryDefaults.NoModelDeployment,
                error: exception.Message);
            return failure;
        }

        var evidence = sources.Select(chunk => new EvidenceSource(sourceType, chunk)).ToList();
        var result = ParseResponse(response, evidence, profile);
        RecordTelemetry(
            request,
            profile,
            sources,
            stopwatch,
            result.Outcome.ToString(),
            modelDeployment: response.ModelDeployment,
            usage: response.Usage);
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

    /// <summary>
    /// Emits one telemetry record for an AI request, whatever its outcome.
    /// </summary>
    /// <remarks>
    /// Wrapped so a telemetry failure can never fail an answer: this is an
    /// observability concern, and a reviewer losing their answer because a log
    /// sink was unavailable would be a far worse outcome than a missing record.
    /// </remarks>
    private void RecordTelemetry(
        QuestionRequest request,
        ScopeProfile profile,
        IReadOnlyList<RetrievedChunk> sources,
        Stopwatch stopwatch,
        string responseStatus,
        string modelDeployment,
        ModelUsage? usage = null,
        string? error = null)
    {
        if (_telemetryLogger is null)
        {
            return;
        }

        try
        {
            _telemetryLogger.LogAiRequest(new AiRequestTelemetry
            {
                RequestId = _requestContext?.CorrelationId ?? AiTelemetryDefaults.NoRequestId,
                UserId = _requestContext?.UserId,
                CaseId = request.Scope == QuestionScope.Case ? request.CaseId : null,
                Question = request.Question,
                ModelDeployment = modelDeployment,
                PromptVersion = profile.PromptVersion,

                // The literal OData filter is built inside the retrieval
                // implementation and is not surfaced by IRetrievalService, so it
                // is left unset rather than guessed at. CaseId above already
                // records the scope an auditor needs.
                SearchFilters = null,

                RetrievedChunkIds = [.. sources.Select(chunk => chunk.ChunkId)],
                RetrievalScores = [.. sources.Select(chunk => chunk.Score)],
                RerankingScores = BuildRerankingScores(sources),
                LatencyMilliseconds = stopwatch.ElapsedMilliseconds,
                PromptTokens = usage?.InputTokens,
                CompletionTokens = usage?.OutputTokens,
                ResponseStatus = responseStatus,
                Error = error,
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to record AI request telemetry.");
        }
    }

    /// <summary>
    /// Reranking scores, but only when every retrieved chunk carries one.
    /// </summary>
    /// <remarks>
    /// The contract is that this list aligns positionally with the chunk ids.
    /// A partially reranked set cannot satisfy that without inventing a score
    /// for the gaps, so it reports an empty list instead — "reranking did not
    /// run here" is true and useful, a fabricated 0.0 is neither.
    /// </remarks>
    private static IReadOnlyList<double> BuildRerankingScores(IReadOnlyList<RetrievedChunk> sources) =>
        sources.Count > 0 && sources.All(chunk => chunk.RerankerScore is not null)
            ? [.. sources.Select(chunk => chunk.RerankerScore!.Value)]
            : [];

    private QuestionResponse ParseResponse(
        ModelResponse response,
        IReadOnlyList<EvidenceSource> sources,
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
        IReadOnlyList<EvidenceSource> sources,
        string modelDeployment,
        ScopeProfile profile)
    {
        var citations = CitationResolver.Resolve(root, sources);

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
