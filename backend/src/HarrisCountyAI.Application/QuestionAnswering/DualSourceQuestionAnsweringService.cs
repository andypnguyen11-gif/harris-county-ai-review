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
/// Default <see cref="IDualSourceQuestionAnsweringService"/>: answers "does
/// what this applicant submitted meet what Harris County requires?" from both
/// corpora in one request.
///
/// The corpora stay separate everywhere it matters. Two independent retrievals
/// run, each under its own mandatory scope filter — the county call carries no
/// case id and can never reach case documents, the case call carries this
/// case's id and can never reach the corpus or another case. The two evidence
/// sets are then presented to the model in two distinctly labeled blocks (see
/// <see cref="ComparisonPrompt"/>), and every citation is tagged with the
/// corpus its passage was retrieved from — assigned here from the scope used,
/// never from anything the model claimed.
///
/// Fail-closed throughout, matching <see cref="QuestionAnsweringService"/>: a
/// comparison needs both sides, so an empty result from either corpus yields
/// insufficient evidence without a model call; an answer that cites nothing
/// resolvable, or that never cites a county source for the requirement it
/// asserts, is downgraded to insufficient evidence; model and parsing failures
/// yield <see cref="QuestionAnswerOutcome.Failed"/> rather than an unverifiable
/// comparison.
/// </summary>
public sealed class DualSourceQuestionAnsweringService : IDualSourceQuestionAnsweringService
{
    /// <summary>Cap on model output; a comparison is short prose plus a citation list.</summary>
    private const int MaxAnswerOutputTokens = 1200;

    /// <summary>Cap on the answer text surfaced to reviewers.</summary>
    private const int MaxAnswerLength = 4000;

    private const string NoCountyEvidenceMessage =
        "No relevant Harris County reference material was found, so what the applicant submitted "
        + "cannot be compared against a county requirement.";

    private const string NoCaseEvidenceMessage =
        "No relevant content was found in this case's submitted documents, so the submission "
        + "cannot be compared against the county requirements.";

    private const string NoEvidenceAtAllMessage =
        "Neither the Harris County reference corpus nor this case's submitted documents returned "
        + "relevant material, so the comparison could not be made.";

    private const string ModelInsufficientMessage =
        "The available county reference material and submitted case documents do not contain "
        + "enough information to compare this submission against the county requirements.";

    private readonly IRetrievalService _retrievalService;
    private readonly ILanguageModelService _languageModel;
    private readonly ILogger<DualSourceQuestionAnsweringService> _logger;
    private readonly IAiRequestTelemetryLogger? _telemetryLogger;
    private readonly IRequestContextAccessor? _requestContext;

    public DualSourceQuestionAnsweringService(
        IRetrievalService retrievalService,
        ILanguageModelService languageModel,
        ILogger<DualSourceQuestionAnsweringService>? logger = null,
        IAiRequestTelemetryLogger? telemetryLogger = null,
        IRequestContextAccessor? requestContext = null)
    {
        ArgumentNullException.ThrowIfNull(retrievalService);
        ArgumentNullException.ThrowIfNull(languageModel);

        _retrievalService = retrievalService;
        _languageModel = languageModel;
        _logger = logger ?? NullLogger<DualSourceQuestionAnsweringService>.Instance;
        _telemetryLogger = telemetryLogger;
        _requestContext = requestContext;
    }

    public async Task<DualSourceQuestionResponse> CompareAsync(
        DualSourceQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("The question must not be empty.", nameof(request));
        }

        if (request.CaseId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dual-source comparison requires the id of the case it is about.", nameof(request));
        }

        // Latency is measured end to end, both retrievals included, because
        // that is what a reviewer waiting on the comparison experiences.
        var stopwatch = Stopwatch.StartNew();

        // Two separate retrievals, each with its own mandatory scope filter.
        // The county request deliberately carries no case id; the case request
        // deliberately carries no corpus metadata filters.
        var countySources = await _retrievalService.RetrieveAsync(
            new RetrievalRequest
            {
                Query = request.Question,
                TopK = request.CountyTopK,
                Scope = SourceType.County,
                CaseId = null,
                PermitType = request.PermitType,
                Department = request.Department,
            },
            cancellationToken);

        var caseSources = await _retrievalService.RetrieveAsync(
            new RetrievalRequest
            {
                Query = request.Question,
                TopK = request.CaseTopK,
                Scope = SourceType.Case,
                CaseId = request.CaseId,
            },
            cancellationToken);

        if (countySources.Count == 0 || caseSources.Count == 0)
        {
            // A comparison has two sides. With only one of them in hand the
            // honest result is "not enough evidence", not a one-sided answer
            // dressed up as a comparison.
            var message = (countySources.Count, caseSources.Count) switch
            {
                (0, 0) => NoEvidenceAtAllMessage,
                (0, _) => NoCountyEvidenceMessage,
                _ => NoCaseEvidenceMessage,
            };

            _logger.LogInformation(
                "Dual-source comparison retrieved {CountyCount} county and {CaseCount} case passages; "
                + "skipping the model call.",
                countySources.Count,
                caseSources.Count);

            var insufficient = Insufficient(message, countySources.Count, caseSources.Count);
            RecordTelemetry(
                request,
                countySources,
                caseSources,
                stopwatch,
                insufficient.Outcome.ToString(),
                modelDeployment: AiTelemetryDefaults.NoModelDeployment);
            return insufficient;
        }

        var modelRequest = new ModelRequest
        {
            SystemPrompt = ComparisonPrompt.SystemPrompt,
            UserPrompt = ComparisonPrompt.BuildUserPrompt(request.Question, countySources, caseSources),
            ExpectsJsonResponse = true,
            JsonResponseSchemaName = ComparisonPrompt.ResponseSchemaName,
            MaxOutputTokens = MaxAnswerOutputTokens,
            PromptVersion = ComparisonPrompt.Version,
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
            _logger.LogWarning(exception, "Dual-source comparison failed to call the language model.");
            var failure = Failed(
                "The language model could not be reached, so the comparison was not made.",
                countySources.Count,
                caseSources.Count);
            RecordTelemetry(
                request,
                countySources,
                caseSources,
                stopwatch,
                failure.Outcome.ToString(),
                modelDeployment: AiTelemetryDefaults.NoModelDeployment,
                error: exception.Message);
            return failure;
        }

        // Source numbering is continuous, county block first, exactly as the
        // prompt presents it — so number N resolves to the same passage the
        // model saw as [N], and its corpus is known here rather than guessed.
        var evidence = new List<EvidenceSource>(countySources.Count + caseSources.Count);
        evidence.AddRange(countySources.Select(chunk => new EvidenceSource(SourceType.County, chunk)));
        evidence.AddRange(caseSources.Select(chunk => new EvidenceSource(SourceType.Case, chunk)));

        var result = ParseResponse(response, evidence, countySources.Count, caseSources.Count);
        RecordTelemetry(
            request,
            countySources,
            caseSources,
            stopwatch,
            result.Outcome.ToString(),
            modelDeployment: response.ModelDeployment,
            usage: response.Usage);
        _logger.LogInformation(
            "Dual-source comparison concluded {Outcome} with {CitationCount} citations from "
            + "{CountyCount} county and {CaseCount} case passages (deployment {Deployment}, prompt {PromptVersion}).",
            result.Outcome,
            result.Citations.Count,
            countySources.Count,
            caseSources.Count,
            response.ModelDeployment,
            ComparisonPrompt.Version);
        return result;
    }

    /// <summary>
    /// Emits one telemetry record for a comparison, whatever its outcome.
    /// </summary>
    /// <remarks>
    /// Chunk ids are recorded county block first, then case block — the same
    /// order the prompt presents them in and the same order citation numbers
    /// resolve against, so position N in this list is the passage the model
    /// saw as source N. Wrapped so a telemetry failure can never fail a
    /// comparison.
    /// </remarks>
    private void RecordTelemetry(
        DualSourceQuestionRequest request,
        IReadOnlyList<RetrievedChunk> countySources,
        IReadOnlyList<RetrievedChunk> caseSources,
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
            var allSources = new List<RetrievedChunk>(countySources.Count + caseSources.Count);
            allSources.AddRange(countySources);
            allSources.AddRange(caseSources);

            _telemetryLogger.LogAiRequest(new AiRequestTelemetry
            {
                RequestId = _requestContext?.CorrelationId ?? AiTelemetryDefaults.NoRequestId,
                UserId = _requestContext?.UserId,
                CaseId = request.CaseId,
                Question = request.Question,
                ModelDeployment = modelDeployment,
                PromptVersion = ComparisonPrompt.Version,

                // As in the single-scope path, the literal OData filter stays
                // inside the retrieval implementation and is not guessed at here.
                SearchFilters = null,

                RetrievedChunkIds = [.. allSources.Select(chunk => chunk.ChunkId)],
                RetrievalScores = [.. allSources.Select(chunk => chunk.Score)],
                RerankingScores = allSources.Count > 0
                    && allSources.All(chunk => chunk.RerankerScore is not null)
                        ? [.. allSources.Select(chunk => chunk.RerankerScore!.Value)]
                        : [],
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

    private DualSourceQuestionResponse ParseResponse(
        ModelResponse response,
        IReadOnlyList<EvidenceSource> evidence,
        int countyEvidenceCount,
        int caseEvidenceCount)
    {
        var json = ExtractJsonObject(response.Content);
        if (json is null)
        {
            _logger.LogWarning("Dual-source comparison response contained no parsable JSON object.");
            return Failed(
                "The model response did not contain the expected JSON answer, so the comparison was not made.",
                countyEvidenceCount,
                caseEvidenceCount,
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
                    "The model response did not include a status in the expected shape, so the comparison was not made.",
                    countyEvidenceCount,
                    caseEvidenceCount,
                    response.ModelDeployment);
            }

            var answer = ReadAnswer(root);
            return NormalizeStatus(statusElement.GetString()) switch
            {
                "answered" => BuildAnsweredResponse(
                    root, answer, evidence, countyEvidenceCount, caseEvidenceCount, response.ModelDeployment),
                "insufficientevidence" => Insufficient(
                    string.IsNullOrWhiteSpace(answer) ? ModelInsufficientMessage : answer,
                    countyEvidenceCount,
                    caseEvidenceCount,
                    response.ModelDeployment),
                var status => FailedForUnknownStatus(
                    status, countyEvidenceCount, caseEvidenceCount, response.ModelDeployment),
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Dual-source comparison response was not valid JSON.");
            return Failed(
                "The model response was not valid JSON, so the comparison was not made.",
                countyEvidenceCount,
                caseEvidenceCount,
                response.ModelDeployment);
        }
    }

    private DualSourceQuestionResponse BuildAnsweredResponse(
        JsonElement root,
        string answer,
        IReadOnlyList<EvidenceSource> evidence,
        int countyEvidenceCount,
        int caseEvidenceCount,
        string modelDeployment)
    {
        var citations = CitationResolver.Resolve(root, evidence);
        var citesCounty = citations.Any(citation => citation.Source == SourceType.County);

        // A comparison always asserts what the county requires, and that
        // assertion has to be traceable to the corpus. Case citations are not
        // demanded in turn: a correct comparison may report that the submission
        // does not show a required item, and an absence has no passage to cite.
        if (string.IsNullOrWhiteSpace(answer) || citations.Count == 0 || !citesCounty)
        {
            _logger.LogWarning(
                "Dual-source comparison resolved {CitationCount} citations (county-cited: {CitesCounty}) "
                + "with answer length {AnswerLength}; downgrading to insufficient evidence.",
                citations.Count,
                citesCounty,
                answer.Length);
            return Insufficient(
                ModelInsufficientMessage, countyEvidenceCount, caseEvidenceCount, modelDeployment);
        }

        return new DualSourceQuestionResponse
        {
            Outcome = QuestionAnswerOutcome.Answered,
            Answer = answer,
            Citations = citations,
            CountyEvidenceCount = countyEvidenceCount,
            CaseEvidenceCount = caseEvidenceCount,
            PromptVersion = ComparisonPrompt.Version,
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

    private DualSourceQuestionResponse FailedForUnknownStatus(
        string status,
        int countyEvidenceCount,
        int caseEvidenceCount,
        string modelDeployment)
    {
        _logger.LogWarning("Dual-source comparison returned unrecognized status '{Status}'.", status);
        return Failed(
            "The model returned an unrecognized status, so the comparison was not made.",
            countyEvidenceCount,
            caseEvidenceCount,
            modelDeployment);
    }

    private static DualSourceQuestionResponse Insufficient(
        string message,
        int countyEvidenceCount,
        int caseEvidenceCount,
        string? modelDeployment = null) => new()
        {
            Outcome = QuestionAnswerOutcome.InsufficientEvidence,
            Answer = message,
            Citations = [],
            CountyEvidenceCount = countyEvidenceCount,
            CaseEvidenceCount = caseEvidenceCount,
            PromptVersion = ComparisonPrompt.Version,
            ModelDeployment = modelDeployment,
        };

    private static DualSourceQuestionResponse Failed(
        string explanation,
        int countyEvidenceCount,
        int caseEvidenceCount,
        string? modelDeployment = null) => new()
        {
            Outcome = QuestionAnswerOutcome.Failed,
            Answer = explanation,
            Citations = [],
            CountyEvidenceCount = countyEvidenceCount,
            CaseEvidenceCount = caseEvidenceCount,
            PromptVersion = ComparisonPrompt.Version,
            ModelDeployment = modelDeployment,
        };
}
