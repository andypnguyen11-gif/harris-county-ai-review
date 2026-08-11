namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Thrown when an embedding request fails permanently — either a non-retryable error
/// or a transient error that persisted through all retry attempts.
/// </summary>
public sealed class EmbeddingServiceException : Exception
{
    public EmbeddingServiceException(string message)
        : base(message)
    {
    }

    public EmbeddingServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
