namespace HarrisCountyAI.Application.Common.Exceptions;

/// <summary>
/// Marks an exception as "a dependency outside this application failed",
/// which the API layer turns into a 5xx that blames the dependency rather
/// than the caller.
/// </summary>
/// <remarks>
/// This is an interface rather than a base class on purpose: the timeout
/// variant needs to stay a <see cref="TimeoutException"/> so that existing
/// timeout handling — and any <c>catch (TimeoutException)</c> a caller
/// already has — keeps working, while still carrying the service name.
/// <para>
/// The <see cref="Exception.Message"/> of an implementation is written for
/// logs, and the inner exception it wraps may carry endpoint names or request
/// URIs. Neither is ever copied into an HTTP response body; the API layer
/// composes its own client-facing text from <see cref="ServiceName"/> alone.
/// </para>
/// </remarks>
public interface IExternalServiceException
{
    /// <summary>
    /// Display name of the failed dependency, from
    /// <see cref="ExternalServiceNames"/>. Safe to show a client.
    /// </summary>
    string ServiceName { get; }
}
