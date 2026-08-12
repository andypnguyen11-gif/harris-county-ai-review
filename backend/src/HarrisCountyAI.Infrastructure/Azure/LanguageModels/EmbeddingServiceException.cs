using HarrisCountyAI.Application.Common.Exceptions;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Thrown when an embedding request fails permanently — either a non-retryable error
/// or a transient error that persisted through all retry attempts.
/// </summary>
/// <remarks>
/// Implements <see cref="IExternalServiceException"/> so the API reports an
/// embeddings outage the same way it reports every other dependency outage:
/// a 503 naming the capability, with the diagnosis left in the logs.
/// </remarks>
public sealed class EmbeddingServiceException : Exception, IExternalServiceException
{
    public EmbeddingServiceException(string message)
        : base(message)
    {
    }

    public EmbeddingServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <inheritdoc />
    public string ServiceName => ExternalServiceNames.Embeddings;
}
