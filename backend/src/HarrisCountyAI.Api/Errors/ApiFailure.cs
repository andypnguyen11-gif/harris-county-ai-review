using HarrisCountyAI.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;

namespace HarrisCountyAI.Api.Errors;

/// <summary>
/// The client-facing description of a failure: a status, a title, a sentence
/// the reviewer can act on, and — when a dependency was at fault — its display
/// name.
/// </summary>
/// <param name="StatusCode">HTTP status for the response.</param>
/// <param name="Title">Short problem title.</param>
/// <param name="Detail">Sentence shown to the caller. Composed here; never taken from an exception.</param>
/// <param name="ServiceName">Display name of the failed dependency, or null.</param>
/// <param name="RetryAfterSeconds">Value for a <c>Retry-After</c> header, when retrying is worth suggesting.</param>
public sealed record ApiFailure(
    int StatusCode,
    string Title,
    string Detail,
    string? ServiceName = null,
    int? RetryAfterSeconds = null)
{
    /// <summary>
    /// Classifies an exception into the response the client should see.
    /// </summary>
    /// <remarks>
    /// Nothing from <paramref name="exception"/> other than its type and, for
    /// dependency failures, its <see cref="IExternalServiceException.ServiceName"/>
    /// influences the result. Exception messages in this codebase legitimately
    /// carry endpoint names, deployment names, blob paths, and container names,
    /// because they are written for the log. Copying one into a response body
    /// is how those end up in a browser's network tab, so the detail text here
    /// is always written from scratch.
    /// </remarks>
    public static ApiFailure From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // Timed out waiting on something downstream. Worth retrying, but
            // the caller should know the work may already have happened.
            ExternalServiceTimeoutException timeout => new ApiFailure(
                StatusCodes.Status504GatewayTimeout,
                "A required service did not respond in time.",
                $"{Capability(timeout.ServiceName)} took too long to respond, so the request was abandoned. Try again in a moment.",
                timeout.ServiceName,
                RetryAfterSeconds: 10),

            TimeoutException => new ApiFailure(
                StatusCodes.Status504GatewayTimeout,
                "A required service did not respond in time.",
                "The request took too long and was abandoned. Try again in a moment.",
                RetryAfterSeconds: 10),

            // The model answered with something unusable. Not retryable in the
            // "wait and it will heal" sense, so no Retry-After.
            MalformedModelResponseException malformed => new ApiFailure(
                StatusCodes.Status502BadGateway,
                "The AI service returned an unusable response.",
                "The AI service replied, but not in a form this application can verify, so no answer was produced.",
                malformed.ServiceName),

            IExternalServiceException dependency => new ApiFailure(
                StatusCodes.Status503ServiceUnavailable,
                "A required service is temporarily unavailable.",
                $"{Capability(dependency.ServiceName)} could not be reached, so this feature is unavailable right now. "
                    + "Other parts of the application are unaffected.",
                dependency.ServiceName,
                RetryAfterSeconds: 30),

            _ => new ApiFailure(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "The request could not be completed. Quote the correlation id when reporting this."),
        };
    }

    /// <summary>
    /// Renders a service display name as the subject of a sentence, e.g.
    /// "Search" becomes "The Search service".
    /// </summary>
    private static string Capability(string serviceName)
        => string.IsNullOrWhiteSpace(serviceName) ? "A required service" : $"The {serviceName} service";
}
