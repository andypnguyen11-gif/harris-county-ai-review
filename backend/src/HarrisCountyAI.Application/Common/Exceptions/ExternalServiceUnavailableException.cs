namespace HarrisCountyAI.Application.Common.Exceptions;

/// <summary>
/// An external dependency could not serve a request: it refused the
/// connection, returned a server error, throttled us, or rejected our
/// credentials. Raised only after any retry policy has been exhausted, so
/// reaching this exception means the dependency is genuinely unusable right
/// now rather than momentarily busy.
/// </summary>
public sealed class ExternalServiceUnavailableException : Exception, IExternalServiceException
{
    public ExternalServiceUnavailableException(
        string serviceName,
        string message,
        Exception? innerException = null,
        int? statusCode = null)
        : base(message, innerException)
    {
        ServiceName = serviceName;
        StatusCode = statusCode;
    }

    /// <inheritdoc />
    public string ServiceName { get; }

    /// <summary>
    /// HTTP status the dependency returned, when it returned one. Recorded for
    /// logs and metrics; it is never forwarded as the API's own status code,
    /// because the client's request did not fail — ours did.
    /// </summary>
    public int? StatusCode { get; }
}
