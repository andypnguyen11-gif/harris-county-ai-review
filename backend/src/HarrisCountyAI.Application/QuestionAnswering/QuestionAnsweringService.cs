using System.Text.Json;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.QuestionAnswering.Prompts;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// Default <see cref="IQuestionAnsweringService"/>: retrieves corpus evidence,
/// asks the language model for a strictly grounded JSON answer, and resolves
/// the model's source numbers back to citations. Fail-closed throughout —
/// no retrieved evidence yields an insufficient-evidence response without a
/// model call, an answer that cites no retrievable source is downgraded to
/// insufficient evidence, and model or parsing failures yield
/// <see cref="QuestionAnswerOutcome.Failed"/> rather than an unverifiable answer.
/// </summary>
public sealed class QuestionAnsweringService : IQuestionAnsweringService
{
    /// <summary>Cap on model output; grounded answers are short prose plus a citation list.</summary>
    private const int MaxAnswerOutputTokens = 800;

    /// <summary>Cap on the answer text surfaced to reviewers.</summary>
    private const int MaxAnswerLength = 4000;

    private const string NoEvidenceMessage =
        "No relevant Harris County reference material was found for this question.";

    private const string ModelInsufficientMessage =
        "The Harris County reference corpus does not contain enough information to answer this question.";

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

        var sources = await _retrievalService.RetrieveAsync(
            new RetrievalRequest { Query = request.Question, TopK = request.TopK },
            cancellationToken);

        if (sources.Count == 0)
        {
            _logger.LogInformation("Corpus Q&A retrieved no evidence; skipping the model call.");
            return new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                Answer = NoEvidenceMessage,
                Citations = [],
                PromptVersion = GroundedQuestionPrompt.Version,
            };
        }

        var modelRequest = new ModelRequest
        {
            SystemPrompt = GroundedQuestionPrompt.SystemPrompt,
            UserPrompt = GroundedQuestionPrompt.BuildUserPrompt(request.Question, sources),
            ExpectsJsonResponse = true,
            JsonResponseSchemaName = GroundedQuestionPrompt.ResponseSchemaName,
            MaxOutputTokens = MaxAnswerOutputTokens,
            PromptVersion = GroundedQuestionPrompt.Version,
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
            _logger.LogWarning(exception, "Corpus Q&A failed to call the language model.");
            return Failed("The language model could not be reached, so the question was not answered.");
        }

        var result = ParseResponse(response, sources);
        _logger.LogInformation(
            "Corpus Q&A concluded {Outcome} with {CitationCount} citations from {SourceCount} sources "
            + "(deployment {Deployment}, prompt {PromptVersion}).",
            result.Outcome,
            result.Citations.Count,
            sources.Count,
            response.ModelDeployment,
            GroundedQuestionPrompt.Version);
        return result;
    }

    private QuestionResponse ParseResponse(ModelResponse response, IReadOnlyList<RetrievedChunk> sources)
    {
        var json = ExtractJsonObject(response.Content);
        if (json is null)
        {
            _logger.LogWarning("Corpus Q&A response contained no parsable JSON object.");
            return Failed(
                "The model response did not contain the expected JSON answer, so the question was not answered.",
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
                    response.ModelDeployment);
            }

            var answer = ReadAnswer(root);
            return NormalizeStatus(statusElement.GetString()) switch
            {
                "answered" => BuildAnsweredResponse(root, answer, sources, response.ModelDeployment),
                "insufficientevidence" => new QuestionResponse
                {
                    Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                    Answer = string.IsNullOrWhiteSpace(answer) ? ModelInsufficientMessage : answer,
                    Citations = [],
                    PromptVersion = GroundedQuestionPrompt.Version,
                    ModelDeployment = response.ModelDeployment,
                },
                var status => FailedForUnknownStatus(status, response.ModelDeployment),
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Corpus Q&A response was not valid JSON.");
            return Failed(
                "The model response was not valid JSON, so the question was not answered.",
                response.ModelDeployment);
        }
    }

    private QuestionResponse BuildAnsweredResponse(
        JsonElement root,
        string answer,
        IReadOnlyList<RetrievedChunk> sources,
        string modelDeployment)
    {
        var citations = ResolveCitations(root, sources);

        if (string.IsNullOrWhiteSpace(answer) || citations.Count == 0)
        {
            // An answer that cites nothing verifiable is not a grounded answer.
            // Fail closed to insufficient evidence rather than presenting it.
            _logger.LogWarning(
                "Corpus Q&A answer had {CitationCount} resolvable citations and answer length {AnswerLength}; "
                + "downgrading to insufficient evidence.",
                citations.Count,
                answer.Length);
            return new QuestionResponse
            {
                Outcome = QuestionAnswerOutcome.InsufficientEvidence,
                Answer = ModelInsufficientMessage,
                Citations = [],
                PromptVersion = GroundedQuestionPrompt.Version,
                ModelDeployment = modelDeployment,
            };
        }

        return new QuestionResponse
        {
            Outcome = QuestionAnswerOutcome.Answered,
            Answer = answer,
            Citations = citations,
            PromptVersion = GroundedQuestionPrompt.Version,
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

    private QuestionResponse FailedForUnknownStatus(string status, string modelDeployment)
    {
        _logger.LogWarning("Corpus Q&A returned unrecognized status '{Status}'.", status);
        return Failed(
            "The model returned an unrecognized status, so the question was not answered.",
            modelDeployment);
    }

    private static QuestionResponse Failed(string explanation, string? modelDeployment = null) => new()
    {
        Outcome = QuestionAnswerOutcome.Failed,
        Answer = explanation,
        Citations = [],
        PromptVersion = GroundedQuestionPrompt.Version,
        ModelDeployment = modelDeployment,
    };
}
