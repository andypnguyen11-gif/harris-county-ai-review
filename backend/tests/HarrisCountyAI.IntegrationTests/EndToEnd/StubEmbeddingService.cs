using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Infrastructure.Azure.Search;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// Stands in for the Azure embedding deployment: returns a fixed vector of the
/// index's dimensionality for every input, so indexing and retrieval run their
/// real code paths without an embedding model. Relevance ranking is therefore
/// not exercised — the in-memory index returns every chunk the scope filter
/// admits, which is what these tests assert about.
/// </summary>
public sealed class StubEmbeddingService : IEmbeddingService
{
    /// <summary>Inputs embedded so far, in call order.</summary>
    public List<string> Inputs { get; } = [];

    public Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        Inputs.AddRange(inputs);

        return Task.FromResult<IReadOnlyList<EmbeddingResult>>(
        [
            .. inputs.Select((_, index) => new EmbeddingResult(Vector(), index, "stub-embedding-model")),
        ]);
    }

    private static float[] Vector()
    {
        var vector = new float[SearchIndexDefinition.EmbeddingDimensions];
        vector[0] = 1f;
        return vector;
    }
}
