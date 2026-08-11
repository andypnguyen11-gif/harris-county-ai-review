using HarrisCountyAI.Application.Search.Embeddings;

namespace HarrisCountyAI.UnitTests.Search.Retrieval;

/// <summary>
/// In-memory <see cref="IEmbeddingService"/> that records every input batch and
/// returns a fixed vector per input.
/// </summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    public List<IReadOnlyList<string>> ReceivedBatches { get; } = [];

    /// <summary>Vector returned for every input.</summary>
    public float[] VectorToReturn { get; set; } = [0.1f, 0.2f, 0.3f];

    /// <summary>When true, <see cref="EmbedAsync"/> returns no results regardless of inputs.</summary>
    public bool ReturnEmpty { get; set; }

    public Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken ct)
    {
        ReceivedBatches.Add(inputs);

        IReadOnlyList<EmbeddingResult> results = ReturnEmpty
            ? []
            : [.. inputs.Select((_, index) => new EmbeddingResult(VectorToReturn, index, "fake-embedding-model"))];
        return Task.FromResult(results);
    }
}
