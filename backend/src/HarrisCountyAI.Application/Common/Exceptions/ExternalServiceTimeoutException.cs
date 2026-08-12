namespace HarrisCountyAI.Application.Common.Exceptions;

/// <summary>
/// An external dependency did not answer within the timeout budget configured
/// for it. Distinct from <see cref="ExternalServiceUnavailableException"/>
/// because a timeout says nothing about whether the work was done — a retry
/// may duplicate it — so only idempotent operations should be retried after
/// one.
/// </summary>
/// <remarks>
/// Derives from <see cref="TimeoutException"/> so that callers already
/// catching timeouts keep working unchanged.
/// </remarks>
public sealed class ExternalServiceTimeoutException : TimeoutException, IExternalServiceException
{
    public ExternalServiceTimeoutException(
        string serviceName,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ServiceName = serviceName;
    }

    /// <inheritdoc />
    public string ServiceName { get; }
}
