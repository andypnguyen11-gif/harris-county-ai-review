using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using Azure.AI.OpenAI;
using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// <see cref="ILanguageModelService"/> implementation backed by an Azure OpenAI
/// chat-completions deployment. Enforces a configurable per-request timeout,
/// propagates caller cancellation, captures token usage, and emits structured
/// logs (prompt contents are logged at Debug level only, never at Information).
/// </summary>
/// <remarks>
/// Three failure shapes are distinguished, because callers respond to them
/// differently: the endpoint refusing or erroring becomes an
/// <see cref="ExternalServiceUnavailableException"/>, exceeding the request
/// budget becomes an <see cref="ExternalServiceTimeoutException"/>, and a
/// completion carrying no text at all becomes a
/// <see cref="MalformedModelResponseException"/> rather than an empty
/// <see cref="ModelResponse"/> that every caller would then have to
/// re-diagnose.
/// </remarks>
public class AzureLanguageModelService : ILanguageModelService
{
    private readonly LanguageModelOptions _options;
    private readonly AzureResilienceOptions _resilience;
    private readonly ILogger<AzureLanguageModelService> _logger;
    private readonly Lazy<ChatClient> _chatClient;

    public AzureLanguageModelService(
        IOptions<LanguageModelOptions> options,
        ILogger<AzureLanguageModelService> logger,
        IOptions<AzureResilienceOptions>? resilience = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _resilience = resilience?.Value ?? new AzureResilienceOptions();
        _logger = logger;
        _chatClient = new Lazy<ChatClient>(CreateChatClient);
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The caller's budget covers the answer; the reserve covers the reasoning
        // the deployment does before writing any of it. See
        // LanguageModelOptions.ReasoningTokenReserve.
        var answerTokens = request.MaxOutputTokens ?? _options.MaxOutputTokens;
        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = answerTokens + _options.ReasoningTokenReserve,
        };

        if (_options.SupportsTemperature)
        {
            // Omitted rather than clamped for a model that has only one: a
            // reasoning model rejects the whole request over a temperature it
            // does not support, so the way to ask for its default is silence.
            chatOptions.Temperature = request.Temperature;
        }

        if (request.ExpectsJsonResponse)
        {
            chatOptions.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
        }

        List<ChatMessage> messages =
        [
            new SystemChatMessage(request.SystemPrompt),
            new UserChatMessage(request.UserPrompt),
        ];

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            // Prompt contents are Debug-only; they never appear at Information level.
            _logger.LogDebug(
                "Sending language model request. Deployment={Deployment} PromptVersion={PromptVersion} "
                + "JsonSchema={JsonResponseSchemaName} SystemPrompt={SystemPrompt} UserPrompt={UserPrompt}",
                _options.Deployment,
                request.PromptVersion,
                request.JsonResponseSchemaName,
                request.SystemPrompt,
                request.UserPrompt);
        }

        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion;
        try
        {
            completion = await CompleteChatAsync(messages, chatOptions, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Language model request canceled by caller. Deployment={Deployment} ElapsedMs={ElapsedMs}",
                _options.Deployment,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (OperationCanceledException innerException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Language model request timed out. Deployment={Deployment} TimeoutSeconds={TimeoutSeconds} ElapsedMs={ElapsedMs}",
                _options.Deployment,
                _options.TimeoutSeconds,
                stopwatch.Elapsed.TotalMilliseconds);

            throw new ExternalServiceTimeoutException(
                ExternalServiceNames.LanguageModel,
                $"Language model request to deployment '{_options.Deployment}' timed out after {_options.TimeoutSeconds}s.",
                innerException);
        }
        catch (ClientResultException exception)
        {
            // The endpoint answered with a failure — throttled, erroring, or
            // rejecting our credentials — after the SDK exhausted its retries.
            _logger.LogWarning(
                exception,
                "Language model request failed. Deployment={Deployment} Status={Status} Transient={Transient} ElapsedMs={ElapsedMs}",
                _options.Deployment,
                exception.Status,
                TransientFailureClassifier.IsTransient(exception),
                stopwatch.Elapsed.TotalMilliseconds);

            throw new ExternalServiceUnavailableException(
                ExternalServiceNames.LanguageModel,
                $"Language model deployment '{_options.Deployment}' returned status {exception.Status}.",
                exception,
                exception.Status);
        }

        stopwatch.Stop();

        var content = string.Concat(completion.Content.Select(part => part.Text));
        if (string.IsNullOrWhiteSpace(content))
        {
            // An empty completion is not a usable answer. Failing here keeps
            // every caller from having to recognise the same empty string.
            _logger.LogWarning(
                "Language model returned an empty completion. Deployment={Deployment} FinishReason={FinishReason} "
                + "AnswerTokenBudget={AnswerTokens} ReasoningTokenReserve={ReasoningReserve} ElapsedMs={ElapsedMs}",
                _options.Deployment,
                completion.FinishReason,
                answerTokens,
                _options.ReasoningTokenReserve,
                stopwatch.Elapsed.TotalMilliseconds);

            // A reasoning model that runs out of tokens mid-thought has written
            // nothing yet, so this reads as a malformed response rather than a
            // truncated one. Naming the budget saves the next reader the trip
            // through the SDK to work out why an empty string came back.
            var cause = completion.FinishReason == ChatFinishReason.Length
                ? $" — the {answerTokens} token answer budget plus a reasoning reserve of "
                  + $"{_options.ReasoningTokenReserve} ran out before any answer was written; "
                  + "raise LanguageModel:ReasoningTokenReserve"
                : string.Empty;

            throw new MalformedModelResponseException(
                $"Language model deployment '{_options.Deployment}' returned no content "
                + $"(finish reason '{completion.FinishReason}'){cause}.");
        }

        var usage = completion.Usage is null
            ? ModelUsage.Empty
            : new ModelUsage(completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount);

        var response = new ModelResponse
        {
            Content = content,
            FinishReason = completion.FinishReason.ToString(),
            Usage = usage,
            ModelDeployment = _options.Deployment,
            Elapsed = stopwatch.Elapsed,
        };

        _logger.LogInformation(
            "Language model request completed. Deployment={Deployment} FinishReason={FinishReason} "
            + "InputTokens={InputTokens} OutputTokens={OutputTokens} ElapsedMs={ElapsedMs} PromptVersion={PromptVersion}",
            _options.Deployment,
            response.FinishReason,
            usage.InputTokens,
            usage.OutputTokens,
            stopwatch.Elapsed.TotalMilliseconds,
            request.PromptVersion);

        return response;
    }

    /// <summary>
    /// Performs the underlying chat-completions network call. Virtual so unit tests
    /// can substitute the network round trip without contacting Azure.
    /// </summary>
    protected virtual async Task<ChatCompletion> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken)
    {
        ClientResult<ChatCompletion> result = await _chatClient.Value
            .CompleteChatAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        return result.Value;
    }

    private ChatClient CreateChatClient()
    {
        // The SDK retries transient failures (429 and 5xx) within the request's
        // own timeout budget; anything left over surfaces as a
        // ClientResultException that GenerateAsync translates.
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = ChatEndpoint(_options.Endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(_resilience.NetworkTimeoutSeconds),
            RetryPolicy = new ClientRetryPolicy(maxRetries: Math.Max(0, _resilience.MaxRetryAttempts)),
        };

        // The plain client against Azure's endpoint, not AzureOpenAIClient.
        // The token cap is `max_completion_tokens` in the request body, and the
        // Azure client rewrites it to the retired `max_tokens` — at every
        // service version it offers — which a reasoning model rejects outright
        // ("Unsupported parameter: 'max_tokens' ... Use 'max_completion_tokens'
        // instead"), failing every evaluation. Azure's OpenAI-compatible route
        // takes the request as written, and takes the API key as a bearer
        // token, so nothing else about this changes.
        var client = new OpenAIClient(new ApiKeyCredential(_options.ApiKey), clientOptions);
        return client.GetChatClient(_options.Deployment);
    }

    /// <summary>
    /// The OpenAI-compatible route on an Azure OpenAI resource. Built by
    /// appending rather than with <see cref="Uri"/> resolution, which would
    /// drop a path the endpoint already carries.
    /// </summary>
    internal static Uri ChatEndpoint(string resourceEndpoint)
        => new($"{resourceEndpoint.TrimEnd('/')}/openai/v1");
}
