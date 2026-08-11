using HarrisCountyAI.Application.Common.AI;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// Scriptable <see cref="ILanguageModelService"/> for API tests: replays
/// enqueued content (or exceptions) in order, so no real model is called.
/// </summary>
public sealed class ScriptedLanguageModelService : ILanguageModelService
{
    private readonly Queue<Func<ModelRequest, ModelResponse>> _scripted = new();

    public List<ModelRequest> Requests { get; } = [];

    public ScriptedLanguageModelService EnqueueContent(string content)
    {
        _scripted.Enqueue(_ => new ModelResponse
        {
            Content = content,
            FinishReason = "Stop",
            Usage = new ModelUsage(10, 5),
            ModelDeployment = "integration-fake-deployment",
            Elapsed = TimeSpan.Zero,
        });
        return this;
    }

    public ScriptedLanguageModelService EnqueueException(Exception exception)
    {
        _scripted.Enqueue(_ => throw exception);
        return this;
    }

    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_scripted.Count == 0)
        {
            throw new InvalidOperationException("No scripted model response was enqueued.");
        }

        return Task.FromResult(_scripted.Dequeue()(request));
    }
}
