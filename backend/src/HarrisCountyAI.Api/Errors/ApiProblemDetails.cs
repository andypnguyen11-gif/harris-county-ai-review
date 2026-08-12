using Microsoft.AspNetCore.Mvc;

namespace HarrisCountyAI.Api.Errors;

/// <summary>
/// The extensions every error response in this API carries, and the one place
/// they are attached.
/// </summary>
/// <remarks>
/// Error responses are deliberately thin. They say what kind of thing went
/// wrong and, when a dependency was at fault, which capability it was — and
/// nothing else. No exception text, no stack trace, no endpoint, no resource
/// name. The <c>correlationId</c> is what makes that safe: it is the one value
/// that ties the sentence a reviewer sees on screen to the full, unredacted
/// record in the logs, so support can ask for a single short string and find
/// everything.
/// </remarks>
public static class ApiProblemDetails
{
    /// <summary>Extension carrying the request's correlation id.</summary>
    public const string CorrelationIdExtension = "correlationId";

    /// <summary>Extension naming the failed dependency, when one failed.</summary>
    public const string ServiceExtension = "service";

    /// <summary>
    /// Key the correlation-id middleware stores the id under in
    /// <see cref="HttpContext.Items"/>.
    /// </summary>
    /// <remarks>
    /// Matched by value rather than by referencing the middleware's own
    /// constant, so that error handling stays useful whether or not the
    /// correlation-id middleware is in the pipeline — under a test host that
    /// builds a bare <c>DefaultHttpContext</c>, for instance.
    /// </remarks>
    public const string CorrelationIdItemKey = "CorrelationId";

    /// <summary>Header the correlation id is echoed on.</summary>
    public const string CorrelationIdHeaderName = "X-Correlation-Id";

    /// <summary>
    /// The correlation id for this request: the one the correlation-id
    /// middleware assigned, the one echoed on the response, or — when neither
    /// is present — the server's own trace identifier, so the response is
    /// never left without a handle into the logs.
    /// </summary>
    public static string GetCorrelationId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(CorrelationIdItemKey, out var stored)
            && stored is string storedId
            && !string.IsNullOrWhiteSpace(storedId))
        {
            return storedId;
        }

        var header = context.Response.Headers[CorrelationIdHeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(header))
        {
            return header;
        }

        return context.TraceIdentifier;
    }

    /// <summary>
    /// Attaches the correlation id (and the request path as the problem
    /// instance) to a problem document. Safe to call more than once.
    /// </summary>
    public static void Enrich(HttpContext context, ProblemDetails problemDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problemDetails);

        problemDetails.Extensions[CorrelationIdExtension] = GetCorrelationId(context);
        problemDetails.Instance ??= context.Request.Path.Value;
    }
}
