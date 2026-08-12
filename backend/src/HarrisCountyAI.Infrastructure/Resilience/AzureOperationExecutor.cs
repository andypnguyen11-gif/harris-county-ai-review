using System.ClientModel;
using Azure;
using HarrisCountyAI.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.Infrastructure.Resilience;

/// <summary>
/// Runs one call against an Azure dependency and translates whatever the SDK
/// throws into the application's own vocabulary.
/// </summary>
/// <remarks>
/// This is the seam that stops SDK detail from escaping Infrastructure. An
/// <see cref="RequestFailedException"/> carries the request URI, the service
/// endpoint, and sometimes response headers; letting it travel up to the API
/// layer makes it far too easy for one of those to end up in a response body.
/// Everything that leaves here is either an application exception carrying a
/// display name and nothing else, or an exception the caller is expected to
/// handle itself.
/// <para>
/// Retrying happens beneath this method, inside the SDK's own retry policy
/// configured by <see cref="AzureClientResilienceExtensions"/>. By the time a
/// failure surfaces here, the transient attempts are already spent.
/// </para>
/// </remarks>
public static class AzureOperationExecutor
{
    /// <summary>
    /// Executes <paramref name="action"/>, translating dependency failures.
    /// </summary>
    /// <param name="serviceName">Display name from <see cref="ExternalServiceNames"/>.</param>
    /// <param name="operation">Short operation name for logs, e.g. "search".</param>
    /// <param name="action">The dependency call.</param>
    /// <param name="cancellationToken">The caller's token; cancellation through it is never translated.</param>
    /// <param name="logger">Optional logger for the failure record.</param>
    public static async Task<T> ExecuteAsync<T>(
        string serviceName,
        string operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Translate(serviceName, operation, exception, cancellationToken, logger);
        }
    }

    /// <summary>Void-returning overload of <see cref="ExecuteAsync{T}"/>.</summary>
    public static async Task ExecuteAsync(
        string serviceName,
        string operation,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Translate(serviceName, operation, exception, cancellationToken, logger);
        }
    }

    /// <summary>
    /// Returns the exception to throw for <paramref name="exception"/>. Returns
    /// the original instance when the failure is not ours to reinterpret — the
    /// caller cancelled, the resource genuinely does not exist, or the failure
    /// is an application bug that should surface as one.
    /// </summary>
    internal static Exception Translate(
        string serviceName,
        string operation,
        Exception exception,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        // The caller walked away. Not a dependency failure.
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return exception;
        }

        // Already in application vocabulary — a nested call translated it.
        if (exception is IExternalServiceException)
        {
            return exception;
        }

        var status = TransientFailureClassifier.GetStatus(exception);

        // "Not there" is an answer, not an outage. The caller decides what a
        // missing blob or index means; it is not a 503.
        if (status is 404 or 409 or 412)
        {
            return exception;
        }

        if (!IsDependencyFailure(exception))
        {
            // Argument errors, mapping bugs, JSON problems in our own code:
            // leave them alone so they surface as the 500s they are.
            return exception;
        }

        var isTimeout = exception is TimeoutException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            || status == 408;

        logger?.LogWarning(
            exception,
            "{ServiceName} {Operation} failed with status {Status} (transient: {Transient}, timeout: {Timeout}).",
            serviceName,
            operation,
            status,
            TransientFailureClassifier.IsTransient(exception),
            isTimeout);

        if (isTimeout)
        {
            return new ExternalServiceTimeoutException(
                serviceName,
                $"{serviceName} did not respond in time during '{operation}'.",
                exception);
        }

        return new ExternalServiceUnavailableException(
            serviceName,
            $"{serviceName} could not serve '{operation}'"
                + (status is null ? "." : $" (status {status})."),
            exception,
            status);
    }

    /// <summary>
    /// True when the exception describes the dependency failing rather than
    /// this application misusing it.
    /// </summary>
    private static bool IsDependencyFailure(Exception exception) => exception switch
    {
        // Listed before IOException, which they derive from: "the blob is not
        // there" is an answer the caller turns into a 404, not an outage.
        FileNotFoundException => false,
        DirectoryNotFoundException => false,

        RequestFailedException => true,
        ClientResultException => true,
        TimeoutException => true,
        HttpRequestException => true,
        IOException => true,
        OperationCanceledException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsDependencyFailure),
        _ => exception.InnerException is not null && IsDependencyFailure(exception.InnerException),
    };
}
