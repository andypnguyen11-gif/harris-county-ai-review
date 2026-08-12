using HarrisCountyAI.Api.Errors;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace HarrisCountyAI.Api.Middleware;

/// <summary>
/// Last line of defence for anything that escapes a controller: turns it into
/// an RFC 9457 problem document and writes the full story to the log.
/// </summary>
/// <remarks>
/// The split is deliberate. The log gets everything — the exception, its inner
/// chain, the endpoint, the status the dependency returned — under the
/// request's correlation id. The client gets a status, a title, one actionable
/// sentence, the name of the failed capability, and that same correlation id.
/// A caller can therefore report a problem precisely without ever having been
/// shown an Azure endpoint, a key, a connection string, or a stack frame.
/// <para>
/// A dependency failure is reported as 502/503/504, never as a 500: the
/// distinction tells the caller that retrying may work and tells an operator
/// which system to look at. One dependency being down degrades the feature
/// that needs it — the request that touched it fails — and leaves every other
/// endpoint serving normally.
/// </para>
/// </remarks>
public sealed class ExceptionHandlingMiddleware
{
    /// <summary>
    /// Nginx's convention for "the client went away before we answered".
    /// Nothing is written to the socket; the status exists so the request log
    /// distinguishes an abandoned request from a server error.
    /// </summary>
    private const int ClientClosedRequestStatusCode = 499;

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        ProblemDetailsFactory problemDetailsFactory)
    {
        _next = next;
        _logger = logger;
        _problemDetailsFactory = problemDetailsFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var correlationId = ApiProblemDetails.GetCorrelationId(context);

        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // The caller closed the connection. There is nobody to answer, and
            // this is not a fault worth alerting on.
            _logger.LogInformation(
                "Request {RequestMethod} {RequestPath} was abandoned by the client. CorrelationId={CorrelationId}",
                context.Request.Method,
                context.Request.Path.Value,
                correlationId);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ClientClosedRequestStatusCode;
            }

            return;
        }

        var failure = ApiFailure.From(exception);
        Log(context, exception, failure, correlationId);

        if (context.Response.HasStarted)
        {
            // Headers are already on the wire — a streamed file, say. The
            // response cannot be rewritten into a problem document, so the
            // connection is dropped rather than a truncated body being passed
            // off as complete.
            _logger.LogWarning(
                "Response for {RequestMethod} {RequestPath} had already started; aborting the connection. "
                    + "CorrelationId={CorrelationId}",
                context.Request.Method,
                context.Request.Path.Value,
                correlationId);

            context.Abort();
            return;
        }

        await WriteProblemDetailsAsync(context, failure);
    }

    private void Log(HttpContext context, Exception exception, ApiFailure failure, string correlationId)
    {
        // 5xx we caused is an error; a dependency being unhealthy is a warning,
        // because the application behaved correctly by reporting it.
        var level = failure.ServiceName is null ? LogLevel.Error : LogLevel.Warning;

        _logger.Log(
            level,
            exception,
            "Request {RequestMethod} {RequestPath} failed with {StatusCode} ({FailedService}). "
                + "CorrelationId={CorrelationId}",
            context.Request.Method,
            context.Request.Path.Value,
            failure.StatusCode,
            failure.ServiceName ?? "application",
            correlationId);
    }

    private async Task WriteProblemDetailsAsync(HttpContext context, ApiFailure failure)
    {
        context.Response.Clear();
        context.Response.StatusCode = failure.StatusCode;

        if (failure.RetryAfterSeconds is { } retryAfter)
        {
            context.Response.Headers.RetryAfter = retryAfter.ToString();
        }

        var problemDetails = _problemDetailsFactory.CreateProblemDetails(
            context,
            failure.StatusCode,
            failure.Title,
            detail: failure.Detail);

        if (failure.ServiceName is not null)
        {
            problemDetails.Extensions[ApiProblemDetails.ServiceExtension] = failure.ServiceName;
        }

        await context.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json");
    }
}
