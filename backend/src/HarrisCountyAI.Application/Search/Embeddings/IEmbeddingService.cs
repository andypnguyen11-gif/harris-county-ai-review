namespace HarrisCountyAI.Application.Search.Embeddings;

/// <summary>
/// Generates vector embeddings for text inputs. The API is batch-first: callers pass the
/// full list of inputs and implementations are responsible for provider-specific batching.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates one embedding per input. Results are returned in input order, and each
    /// result carries the index of the input it was generated for.
    /// </summary>
    /// <param name="inputs">The texts to embed. May be empty, in which case no calls are made.</param>
    /// <param name="ct">Cancels any in-flight or pending embedding requests.</param>
    Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}
