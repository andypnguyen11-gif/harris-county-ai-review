namespace HarrisCountyAI.Application.Search.Embeddings;

/// <summary>
/// The embedding generated for a single input text.
/// </summary>
/// <param name="Vector">The embedding vector.</param>
/// <param name="InputIndex">The zero-based index of the input this embedding was generated for.</param>
/// <param name="Model">The model that generated the embedding, as reported by the provider.</param>
public sealed record EmbeddingResult(float[] Vector, int InputIndex, string Model);
