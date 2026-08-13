using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Infrastructure.Azure.LanguageModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

public class AzureLanguageModelServiceTests
{
    private static LanguageModelOptions CreateOptions(
        int timeoutSeconds = 60,
        int maxOutputTokens = 1024,
        bool supportsTemperature = true,
        int reasoningTokenReserve = 0) => new()
    {
        Endpoint = "https://unit-test.openai.azure.com/",
        ApiKey = "unit-test-key",
        Deployment = "gpt-unit-test",
        TimeoutSeconds = timeoutSeconds,
        MaxOutputTokens = maxOutputTokens,
        SupportsTemperature = supportsTemperature,
        ReasoningTokenReserve = reasoningTokenReserve,
    };

    private static ModelRequest CreateRequest() => new()
    {
        SystemPrompt = "You are a compliance reviewer.",
        UserPrompt = "Does this explanation satisfy the requirement?",
    };

    private static ChatCompletion CreateCompletion(
        string content = "model output",
        int inputTokens = 120,
        int outputTokens = 45)
        => OpenAIChatModelFactory.ChatCompletion(
            content: new ChatMessageContent(content),
            finishReason: ChatFinishReason.Stop,
            usage: OpenAIChatModelFactory.ChatTokenUsage(
                outputTokenCount: outputTokens,
                inputTokenCount: inputTokens,
                totalTokenCount: inputTokens + outputTokens));

    [Fact]
    public async Task GenerateAsync_Maps_Content_FinishReason_Usage_Deployment_And_Elapsed()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(CreateCompletion("the answer", inputTokens: 120, outputTokens: 45)));

        var response = await service.GenerateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("the answer", response.Content);
        Assert.Equal("Stop", response.FinishReason);
        Assert.Equal(120, response.Usage.InputTokens);
        Assert.Equal(45, response.Usage.OutputTokens);
        Assert.Equal(165, response.Usage.TotalTokens);
        Assert.Equal("gpt-unit-test", response.ModelDeployment);
        Assert.True(response.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_Sends_System_And_User_Prompts_With_Request_Parameters()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        var request = CreateRequest() with { Temperature = 0.3f, MaxOutputTokens = 256 };
        await service.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(service.CapturedMessages);
        Assert.Equal(2, service.CapturedMessages!.Count);
        Assert.IsType<SystemChatMessage>(service.CapturedMessages[0]);
        Assert.IsType<UserChatMessage>(service.CapturedMessages[1]);
        Assert.Equal(request.SystemPrompt, service.CapturedMessages[0].Content[0].Text);
        Assert.Equal(request.UserPrompt, service.CapturedMessages[1].Content[0].Text);
        Assert.Equal(0.3f, service.CapturedOptions!.Temperature);
        Assert.Equal(256, service.CapturedOptions.MaxOutputTokenCount);
    }

    [Theory]
    [InlineData("https://unit-test.openai.azure.com/", "https://unit-test.openai.azure.com/openai/v1")]
    [InlineData("https://unit-test.openai.azure.com", "https://unit-test.openai.azure.com/openai/v1")]
    public void ChatEndpoint_Addresses_The_OpenAI_Compatible_Route(string configured, string expected)
    {
        // Chat goes through this route rather than through AzureOpenAIClient,
        // which rewrites the token cap to the retired `max_tokens` that a
        // reasoning model rejects. Whether the configured endpoint carries a
        // trailing slash is not the operator's problem.
        Assert.Equal(expected, AzureLanguageModelService.ChatEndpoint(configured).ToString());
    }

    [Fact]
    public async Task GenerateAsync_Omits_Temperature_For_A_Deployment_That_Has_Only_One()
    {
        // A reasoning model (o-series, GPT-5) supports only its own default and
        // answers any other with 400 "Unsupported value: 'temperature'", failing
        // the whole evaluation. Asking for its default means saying nothing.
        var service = new TestableAzureLanguageModelService(
            CreateOptions(supportsTemperature: false),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        await service.GenerateAsync(CreateRequest() with { Temperature = 0.1f }, CancellationToken.None);

        Assert.Null(service.CapturedOptions!.Temperature);
        // The token cap is unaffected: it is sent as max_completion_tokens,
        // which a reasoning model does accept.
        Assert.Equal(1024, service.CapturedOptions.MaxOutputTokenCount);
    }

    [Fact]
    public async Task GenerateAsync_Uses_Configured_MaxOutputTokens_When_Request_Omits_It()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(maxOutputTokens: 777),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        await service.GenerateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(777, service.CapturedOptions!.MaxOutputTokenCount);
        Assert.Equal(0.1f, service.CapturedOptions.Temperature);
    }

    [Fact]
    public async Task GenerateAsync_Adds_The_Reasoning_Reserve_To_The_Callers_Answer_Budget()
    {
        // A reasoning model reasons before it writes, and both come out of the
        // same cap. The caller asked for room for an 800-token answer; the
        // deployment needs its thinking paid for on top, or the answer is never
        // reached.
        var service = new TestableAzureLanguageModelService(
            CreateOptions(supportsTemperature: false, reasoningTokenReserve: 2048),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        await service.GenerateAsync(CreateRequest() with { MaxOutputTokens = 800 }, CancellationToken.None);

        Assert.Equal(2848, service.CapturedOptions!.MaxOutputTokenCount);
    }

    [Fact]
    public async Task GenerateAsync_Adds_The_Reasoning_Reserve_To_The_Configured_Default_Too()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(maxOutputTokens: 500, reasoningTokenReserve: 1000),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        await service.GenerateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(1500, service.CapturedOptions!.MaxOutputTokenCount);
    }

    [Fact]
    public async Task GenerateAsync_Sends_The_Budget_Unchanged_When_No_Reserve_Is_Configured()
    {
        // The default: a model that does not reason gets exactly what the caller asked for.
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        await service.GenerateAsync(CreateRequest() with { MaxOutputTokens = 800 }, CancellationToken.None);

        Assert.Equal(800, service.CapturedOptions!.MaxOutputTokenCount);
    }

    [Fact]
    public async Task GenerateAsync_Exhausting_The_Budget_Before_Writing_Names_The_Reserve_To_Raise()
    {
        // The failure this reserve exists to prevent: the model spent the whole
        // cap reasoning and returned nothing. "No content" alone sends the next
        // reader looking for an outage.
        var truncated = OpenAIChatModelFactory.ChatCompletion(
            content: new ChatMessageContent(string.Empty),
            finishReason: ChatFinishReason.Length);

        var service = new TestableAzureLanguageModelService(
            CreateOptions(supportsTemperature: false, reasoningTokenReserve: 128),
            (_, _, _) => Task.FromResult(truncated));

        var exception = await Assert.ThrowsAsync<MalformedModelResponseException>(
            () => service.GenerateAsync(CreateRequest() with { MaxOutputTokens = 800 }, CancellationToken.None));

        Assert.Contains("800 token answer budget", exception.Message);
        Assert.Contains("reasoning reserve of 128", exception.Message);
        Assert.Contains("LanguageModel:ReasoningTokenReserve", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_Empty_Completion_For_Another_Reason_Does_Not_Blame_The_Budget()
    {
        var empty = OpenAIChatModelFactory.ChatCompletion(
            content: new ChatMessageContent(string.Empty),
            finishReason: ChatFinishReason.ContentFilter);

        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(empty));

        var exception = await Assert.ThrowsAsync<MalformedModelResponseException>(
            () => service.GenerateAsync(CreateRequest(), CancellationToken.None));

        Assert.Contains("ContentFilter", exception.Message);
        Assert.DoesNotContain("ReasoningTokenReserve", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_Requests_Json_Response_Format_When_Expected()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(CreateCompletion("{}")));

        var request = CreateRequest() with
        {
            ExpectsJsonResponse = true,
            JsonResponseSchemaName = "semantic-validation-result",
        };
        await service.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(service.CapturedOptions!.ResponseFormat);
    }

    [Fact]
    public async Task GenerateAsync_Leaves_Response_Format_Unset_By_Default()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        await service.GenerateAsync(CreateRequest(), CancellationToken.None);

        Assert.Null(service.CapturedOptions!.ResponseFormat);
    }

    [Fact]
    public async Task GenerateAsync_Throws_TimeoutException_When_Call_Exceeds_Configured_Timeout()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(timeoutSeconds: 1),
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateCompletion();
            });

        var exception = await Assert.ThrowsAsync<ExternalServiceTimeoutException>(
            () => service.GenerateAsync(CreateRequest(), CancellationToken.None));

        // Still a TimeoutException, so callers that already catch one are unaffected.
        Assert.IsAssignableFrom<TimeoutException>(exception);
        Assert.Equal(ExternalServiceNames.LanguageModel, exception.ServiceName);
        Assert.Contains("gpt-unit-test", exception.Message);
        Assert.Contains("1s", exception.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task GenerateAsync_Propagates_Caller_Cancellation_As_OperationCanceledException()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(timeoutSeconds: 600),
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateCompletion();
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GenerateAsync(CreateRequest(), cts.Token));

        Assert.IsNotType<TimeoutException>(exception);
        Assert.True(service.CapturedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task GenerateAsync_Passes_A_Token_Linked_To_The_Caller_Token()
    {
        using var cts = new CancellationTokenSource();
        bool? innerTokenWasCanceledDuringCall = null;
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, cancellationToken) =>
            {
                innerTokenWasCanceledDuringCall = cancellationToken.IsCancellationRequested;
                return Task.FromResult(CreateCompletion());
            });

        cts.Cancel();
        await service.GenerateAsync(CreateRequest(), cts.Token);

        Assert.True(innerTokenWasCanceledDuringCall);
    }

    [Fact]
    public async Task GenerateAsync_Reports_Empty_Usage_When_Provider_Omits_It()
    {
        var completion = OpenAIChatModelFactory.ChatCompletion(
            content: new ChatMessageContent("no usage"),
            finishReason: ChatFinishReason.Stop);
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(completion));

        var response = await service.GenerateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(0, response.Usage.InputTokens);
        Assert.Equal(0, response.Usage.OutputTokens);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(401)]
    public async Task GenerateAsync_Reports_An_Endpoint_Failure_As_A_Dependency_Outage(int status)
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => throw FakeClientResultExceptions.WithStatus(status));

        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => service.GenerateAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(ExternalServiceNames.LanguageModel, exception.ServiceName);
        Assert.Equal(status, exception.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateAsync_Rejects_A_Completion_With_No_Usable_Content(string content)
    {
        // An empty completion is model noise, not an answer. Failing here saves
        // every caller from having to recognise the same empty string.
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(CreateCompletion(content)));

        var exception = await Assert.ThrowsAsync<MalformedModelResponseException>(
            () => service.GenerateAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(ExternalServiceNames.LanguageModel, exception.ServiceName);
    }

    [Fact]
    public async Task GenerateAsync_Throws_On_Null_Request()
    {
        var service = new TestableAzureLanguageModelService(
            CreateOptions(),
            (_, _, _) => Task.FromResult(CreateCompletion()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.GenerateAsync(null!, CancellationToken.None));
    }

    /// <summary>
    /// Substitutes the network round trip so tests never contact Azure, while
    /// exercising the real timeout, cancellation, mapping, and logging logic.
    /// </summary>
    private sealed class TestableAzureLanguageModelService : AzureLanguageModelService
    {
        private readonly Func<IReadOnlyList<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<ChatCompletion>> _completeChat;

        public TestableAzureLanguageModelService(
            LanguageModelOptions options,
            Func<IReadOnlyList<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<ChatCompletion>> completeChat)
            : base(Options.Create(options), NullLogger<AzureLanguageModelService>.Instance)
        {
            _completeChat = completeChat;
        }

        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }

        public ChatCompletionOptions? CapturedOptions { get; private set; }

        public CancellationToken CapturedToken { get; private set; }

        protected override Task<ChatCompletion> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions options,
            CancellationToken cancellationToken)
        {
            CapturedMessages = messages;
            CapturedOptions = options;
            CapturedToken = cancellationToken;
            return _completeChat(messages, options, cancellationToken);
        }
    }
}
