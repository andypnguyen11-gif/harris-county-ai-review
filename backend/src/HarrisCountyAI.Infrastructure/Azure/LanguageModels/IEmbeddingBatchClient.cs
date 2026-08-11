namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Thin seam over the Azure OpenAI embeddings endpoint. It sends exactly one already-sized
/// batch per call so that the batching, retry, and index-mapping logic in
/// <see cref="AzureEmbeddingService"/> can be unit tested without network access.
/// </summary>
public interface IEmbeddingBatchClient
{
    /// <summary>
    /// Sends a single batch of inputs and returns one vector per input, in input order.
    /// </summary>
    Task<EmbeddingBatchResponse> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}

/// <summary>
/// The provider response for a single embedding batch.
/// </summary>
/// <param name="Vectors">One vector per input, in input order.</param>
/// <param name="Model">The model that generated the embeddings, as reported by the provider.</param>
public sealed record EmbeddingBatchResponse(IReadOnlyList<float[]> Vectors, string Model);
