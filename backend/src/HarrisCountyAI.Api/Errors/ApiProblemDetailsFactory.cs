using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Api.Errors;

/// <summary>
/// Replaces MVC's default <see cref="ProblemDetailsFactory"/> so that every
/// problem document the framework produces — automatic model-state 400s,
/// <c>ControllerBase.Problem(...)</c>, <c>ValidationProblem(...)</c> — carries
/// the same correlation id as the ones this application's exception middleware
/// writes.
/// </summary>
/// <remarks>
/// Reproduces the framework's own defaults (status-derived title and type from
/// <see cref="ApiBehaviorOptions.ClientErrorMapping"/>, plus a trace id) rather
/// than replacing them, so the response shape stays exactly the standard
/// RFC 9457 one and only gains extensions.
/// </remarks>
public sealed class ApiProblemDetailsFactory : ProblemDetailsFactory
{
    private const string TraceIdExtension = "traceId";

    private readonly ApiBehaviorOptions _options;

    public ApiProblemDetailsFactory(IOptions<ApiBehaviorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public override ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        statusCode ??= StatusCodes.Status500InternalServerError;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance,
        };

        ApplyDefaults(httpContext, problemDetails, statusCode.Value);

        return problemDetails;
    }

    public override ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext,
        ModelStateDictionary modelStateDictionary,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(modelStateDictionary);

        statusCode ??= StatusCodes.Status400BadRequest;

        var problemDetails = new ValidationProblemDetails(modelStateDictionary)
        {
            Status = statusCode,
            Type = type,
            Detail = detail,
            Instance = instance,
        };

        if (title is not null)
        {
            // ValidationProblemDetails supplies its own default title; only an
            // explicit one replaces it.
            problemDetails.Title = title;
        }

        ApplyDefaults(httpContext, problemDetails, statusCode.Value);

        return problemDetails;
    }

    private void ApplyDefaults(HttpContext? httpContext, ProblemDetails problemDetails, int statusCode)
    {
        if (_options.ClientErrorMapping.TryGetValue(statusCode, out var clientErrorData))
        {
            problemDetails.Title ??= clientErrorData.Title;
            problemDetails.Type ??= clientErrorData.Link;
        }

        var traceId = Activity.Current?.Id ?? httpContext?.TraceIdentifier;
        if (traceId is not null)
        {
            problemDetails.Extensions[TraceIdExtension] = traceId;
        }

        if (httpContext is not null)
        {
            ApiProblemDetails.Enrich(httpContext, problemDetails);
        }
    }
}
