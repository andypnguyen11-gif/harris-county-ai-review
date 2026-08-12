using System.ClientModel;
using System.Net.Sockets;
using Azure;

namespace HarrisCountyAI.Infrastructure.Resilience;

/// <summary>
/// Decides whether a failure is worth trying again.
/// </summary>
/// <remarks>
/// The rule is narrow on purpose. Retrying is only ever right when the same
/// request, sent again, could plausibly succeed: the service was busy, a node
/// was cycling, a packet was lost. A 400 means the request itself is wrong and
/// will be wrong every time; a 401 or 403 means our credentials are wrong and
/// hammering the endpoint just multiplies audit noise; a 404 means the thing
/// is not there. None of those are retried, here or by the Azure SDK's retry
/// policy, which classifies status codes the same way.
/// </remarks>
public static class TransientFailureClassifier
{
    /// <summary>
    /// Status codes that describe a temporarily unhealthy service rather than
    /// a bad request: request timeout, throttling, and the server-side 5xx
    /// family that can differ between attempts. 501 (not implemented) and 505
    /// are excluded — they will not change on a retry.
    /// </summary>
    private static readonly int[] TransientStatusCodes =
        [408, 429, 500, 502, 503, 504, 507, 509];

    /// <summary>
    /// True when the failure is temporary and the same request could succeed
    /// if sent again.
    /// </summary>
    public static bool IsTransient(Exception? exception) => exception switch
    {
        null => false,
        RequestFailedException failure => IsTransientStatus(failure.Status),
        ClientResultException failure => IsTransientStatus(failure.Status),

        // Nothing was heard back at all: the connection dropped, the name did
        // not resolve, the socket timed out. Another attempt is reasonable.
        TimeoutException => true,
        HttpRequestException => true,
        SocketException => true,
        IOException => true,

        AggregateException aggregate => aggregate.InnerExceptions.Any(IsTransient),

        // Deliberately not transient, and listed rather than defaulted so the
        // intent survives future edits: cancellation is the caller's decision,
        // and an argument problem is our own bug.
        OperationCanceledException => false,
        ArgumentException => false,

        _ => IsTransient(exception.InnerException),
    };

    /// <summary>True when an HTTP status describes a temporary condition.</summary>
    public static bool IsTransientStatus(int status)
        // Status 0 means the SDK never got a response — treated the same as a
        // dropped connection.
        => status == 0 || Array.IndexOf(TransientStatusCodes, status) >= 0;

    /// <summary>
    /// True when the dependency rejected our credentials. Never retried:
    /// a wrong key stays wrong, and repeated attempts look like an attack.
    /// </summary>
    public static bool IsAuthenticationFailure(int status) => status is 401 or 403;

    /// <summary>
    /// The HTTP status behind a failure, when the exception carries one.
    /// </summary>
    public static int? GetStatus(Exception? exception) => exception switch
    {
        null => null,
        RequestFailedException failure => failure.Status,
        ClientResultException failure => failure.Status,
        AggregateException aggregate => aggregate.InnerExceptions.Select(GetStatus).FirstOrDefault(s => s is not null),
        _ => GetStatus(exception.InnerException),
    };
}
