using System.Diagnostics;

namespace HarrisCountyAI.Api.Middleware;

/// <summary>
/// Logs one structured event per request with the method, path, response
/// status code, and elapsed time. Failures are logged with the exception and
/// rethrown so the regular error handling still applies.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);

            _logger.LogInformation(
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "HTTP {RequestMethod} {RequestPath} failed after {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path.Value,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
