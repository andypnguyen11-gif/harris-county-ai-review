namespace HarrisCountyAI.Api.Middleware;

/// <summary>
/// Assigns every request a correlation id: an incoming <c>X-Correlation-Id</c>
/// header is honored when well-formed, otherwise a new id is generated. The id
/// is returned on the response, stored in <see cref="HttpContext.Items"/>, and
/// pushed onto the logging scope so every log line written while handling the
/// request carries a <c>CorrelationId</c> property.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>Header used to accept and return the correlation id.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Key under which the id is stored in <see cref="HttpContext.Items"/>.</summary>
    public const string ItemKey = "CorrelationId";

    private const int MaxIncomingLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName].ToString());

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Returns the incoming value when it is usable as a correlation id
    /// (non-empty, within length limits, and free of characters that would
    /// corrupt log output or headers), otherwise a newly generated id.
    /// </summary>
    internal static string ResolveCorrelationId(string? incomingValue)
    {
        var incoming = incomingValue?.Trim();

        if (!string.IsNullOrEmpty(incoming)
            && incoming.Length <= MaxIncomingLength
            && incoming.All(IsSafeCorrelationIdCharacter))
        {
            return incoming;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsSafeCorrelationIdCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':';
}
