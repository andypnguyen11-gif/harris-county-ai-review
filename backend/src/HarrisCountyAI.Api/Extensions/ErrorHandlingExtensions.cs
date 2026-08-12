using HarrisCountyAI.Api.Errors;
using HarrisCountyAI.Api.Middleware;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Api.Extensions;

/// <summary>
/// Composition-root wiring for error handling. Program.cs calls
/// <see cref="AddApiErrorHandling"/> and <see cref="UseApiErrorHandling"/>
/// only.
/// </summary>
/// <remarks>
/// There are three producers of error responses in an ASP.NET Core API, and
/// all three are routed through the same shape here so a client never has to
/// branch on which one answered:
/// <list type="bullet">
/// <item>controllers, via <c>Problem()</c> and <c>ValidationProblem()</c> —
/// covered by <see cref="ApiProblemDetailsFactory"/>;</item>
/// <item>the framework, for statuses set without a body (a 401 challenge, a
/// route that matched nothing) — covered by the status-code-pages middleware
/// writing through <c>IProblemDetailsService</c>;</item>
/// <item>unhandled exceptions — covered by
/// <see cref="ExceptionHandlingMiddleware"/>.</item>
/// </list>
/// </remarks>
public static class ErrorHandlingExtensions
{
    /// <summary>
    /// Registers problem-details services. Must be called after
    /// <c>AddControllers()</c>, because it replaces the
    /// <see cref="ProblemDetailsFactory"/> that MVC registers.
    /// </summary>
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                ApiProblemDetails.Enrich(context.HttpContext, context.ProblemDetails));

        services.Replace(ServiceDescriptor.Singleton<ProblemDetailsFactory, ApiProblemDetailsFactory>());

        return services;
    }

    /// <summary>
    /// Registers the error-handling middleware. Belongs early in the pipeline
    /// — after correlation-id assignment, so error responses can quote the id,
    /// and before everything that might throw.
    /// </summary>
    public static WebApplication UseApiErrorHandling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        // Gives a body to responses the framework produced a bare status for.
        app.UseStatusCodePages();

        return app;
    }
}
