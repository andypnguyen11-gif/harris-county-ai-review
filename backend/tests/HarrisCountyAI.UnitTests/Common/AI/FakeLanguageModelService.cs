using HarrisCountyAI.Application.Common.AI;

namespace HarrisCountyAI.UnitTests.Common.AI;

/// <summary>
/// Scriptable in-memory <see cref="ILanguageModelService"/> for unit tests.
/// Records every request it receives, replays enqueued responses (or exceptions)
/// in order, and can simulate latency that honors cancellation. When nothing is
/// enqueued it returns a default response built from <see cref="DefaultContent"/>.
/// </summary>
public sealed class FakeLanguageModelService : ILanguageModelService
{
    private readonly Queue<Func<ModelRequest, ModelResponse>> _scripted = new();
    private readonly List<ModelRequest> _requests = [];

    /// <summary>Every request received, in call order.</summary>
    public IReadOnlyList<ModelRequest> Requests => _requests;

    /// <summary>The most recent request, or <see langword="null"/> if none were made.</summary>
    public ModelRequest? LastRequest => _requests.Count == 0 ? null : _requests[^1];

    /// <summary>Number of calls made to <see cref="GenerateAsync"/>.</summary>
    public int CallCount => _requests.Count;

    /// <summary>
    /// Artificial latency applied before responding. The delay observes the caller's
    /// cancellation token, so tests can exercise cancellation and timeout paths.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Content returned when no scripted response is queued.</summary>
    public string DefaultContent { get; set; } = "fake response";

    /// <summary>Deployment name stamped on responses this fake builds.</summary>
    public string ModelDeployment { get; set; } = "fake-deployment";

    /// <summary>Queues a fully specified response to return for a future call.</summary>
    public FakeLanguageModelService EnqueueResponse(ModelResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _scripted.Enqueue(_ => response);
        return this;
    }

    /// <summary>Queues a response with the given content and default metadata.</summary>
    public FakeLanguageModelService EnqueueContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _scripted.Enqueue(_ => CreateResponse(content));
        return this;
    }

    /// <summary>Queues a response computed from the request that triggers it.</summary>
    public FakeLanguageModelService EnqueueResponse(Func<ModelRequest, ModelResponse> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        _scripted.Enqueue(responseFactory);
        return this;
    }

    /// <summary>Queues an exception to throw for a future call.</summary>
    public FakeLanguageModelService EnqueueException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _scripted.Enqueue(_ => throw exception);
        return this;
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _requests.Add(request);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return _scripted.Count > 0
            ? _scripted.Dequeue()(request)
            : CreateResponse(DefaultContent);
    }

    /// <summary>Builds a well-formed response with this fake's default metadata.</summary>
    public ModelResponse CreateResponse(
        string content,
        string finishReason = "Stop",
        int inputTokens = 10,
        int outputTokens = 5) => new()
        {
            Content = content,
            FinishReason = finishReason,
            Usage = new ModelUsage(inputTokens, outputTokens),
            ModelDeployment = ModelDeployment,
            Elapsed = Delay,
        };
}
