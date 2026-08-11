using HarrisCountyAI.Infrastructure.Azure.LanguageModels;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Scripted <see cref="IEmbeddingBatchClient"/> for unit tests. Records every batch it
/// receives and either throws the next scripted exception or returns deterministic vectors
/// that encode the batch-local input position.
/// </summary>
internal sealed class FakeEmbeddingBatchClient : IEmbeddingBatchClient
{
    private readonly Queue<Exception> _scriptedFailures = new();

    public List<IReadOnlyList<string>> ReceivedBatches { get; } = [];

    public string Model { get; set; } = "text-embedding-3-small";

    /// <summary>When set, overrides the number of vectors returned for the next call.</summary>
    public int? VectorCountOverride { get; set; }

    /// <summary>When set, the 1-based call with this number throws <see cref="FailureFactory"/>.</summary>
    public int? FailOnCallNumber { get; set; }

    public Func<Exception>? FailureFactory { get; set; }

    public void EnqueueFailure(Exception exception) => _scriptedFailures.Enqueue(exception);

    public Task<EmbeddingBatchResponse> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceivedBatches.Add(inputs.ToArray());

        if (_scriptedFailures.Count > 0)
        {
            throw _scriptedFailures.Dequeue();
        }

        if (FailOnCallNumber == ReceivedBatches.Count && FailureFactory is not null)
        {
            throw FailureFactory();
        }

        var vectorCount = VectorCountOverride ?? inputs.Count;
        var vectors = new float[vectorCount][];
        for (var i = 0; i < vectorCount; i++)
        {
            // Encode the input text's hash and batch-local position so tests can verify
            // that each vector is mapped back to the right input.
            vectors[i] = [i < inputs.Count ? inputs[i].GetHashCode() : 0f, i];
        }

        return Task.FromResult(new EmbeddingBatchResponse(vectors, Model));
    }
}
