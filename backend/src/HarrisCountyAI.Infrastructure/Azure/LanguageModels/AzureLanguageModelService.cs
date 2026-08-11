using System.ClientModel;
using System.Diagnostics;
using Azure.AI.OpenAI;
using HarrisCountyAI.Application.Common.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// <see cref="ILanguageModelService"/> implementation backed by an Azure OpenAI
/// chat-completions deployment. Enforces a configurable per-request timeout,
/// propagates caller cancellation, captures token usage, and emits structured
/// logs (prompt contents are logged at Debug level only, never at Information).
/// </summary>
public class AzureLanguageModelService : ILanguageModelService
{
    private readonly LanguageModelOptions _options;
    private readonly ILogger<AzureLanguageModelService> _logger;
    private readonly Lazy<ChatClient> _chatClient;

    public AzureLanguageModelService(
        IOptions<LanguageModelOptions> options,
        ILogger<AzureLanguageModelService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _chatClient = new Lazy<ChatClient>(CreateChatClient);
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = request.Temperature,
            MaxOutputTokenCount = request.MaxOutputTokens ?? _options.MaxOutputTokens,
        };

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

            throw new TimeoutException(
                $"Language model request to deployment '{_options.Deployment}' timed out after {_options.TimeoutSeconds}s.",
                innerException);
        }

        stopwatch.Stop();

        var usage = completion.Usage is null
            ? ModelUsage.Empty
            : new ModelUsage(completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount);

        var response = new ModelResponse
        {
            Content = string.Concat(completion.Content.Select(part => part.Text)),
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
        var client = new AzureOpenAIClient(new Uri(_options.Endpoint), new ApiKeyCredential(_options.ApiKey));
        return client.GetChatClient(_options.Deployment);
    }
}
