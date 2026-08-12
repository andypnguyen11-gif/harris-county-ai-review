using HarrisCountyAI.Api.Middleware;
using HarrisCountyAI.Api.Telemetry;
using HarrisCountyAI.Application.Common.Telemetry;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Api.Extensions;

/// <summary>
/// Composition-root wiring for observability: structured console logging,
/// optional Application Insights export, and the correlation-id and
/// request-logging middleware. Program.cs calls
/// <see cref="AddObservability"/> and <see cref="UseObservability"/> only.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Configures logging providers (human-readable console in Development,
    /// single-line JSON console elsewhere so log aggregators can parse fields)
    /// and enables Application Insights telemetry when
    /// <c>ApplicationInsights:ConnectionString</c> is configured.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        }
        else
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "O";
                options.UseUtcTimestamp = true;
            });
        }

        // Application Insights is opt-in per environment: locally the
        // connection string stays empty and no telemetry is set up.
        var applicationInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            builder.Services.AddApplicationInsightsTelemetry();
        }

        // Lets the Application layer stamp AI telemetry with the correlation id
        // and the caller's identity without taking a dependency on ASP.NET Core.
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

        return builder;
    }

    /// <summary>
    /// Registers the observability middleware. Runs first in the pipeline so
    /// the correlation id covers every subsequent component and the request
    /// log reflects the final status code.
    /// </summary>
    public static WebApplication UseObservability(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();

        return app;
    }
}
