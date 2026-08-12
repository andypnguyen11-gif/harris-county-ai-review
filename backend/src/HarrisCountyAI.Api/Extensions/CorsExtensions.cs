using HarrisCountyAI.Api.Middleware;
using Microsoft.Net.Http.Headers;

namespace HarrisCountyAI.Api.Extensions;

public static class CorsExtensions
{
    /// <summary>Name of the policy applied to local-development requests.</summary>
    public const string LocalDevelopmentPolicyName = "LocalDevelopment";

    /// <summary>Configuration key holding the origins the policy admits.</summary>
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    /// <summary>
    /// Registers the CORS policy that lets the Angular dev server on
    /// <c>http://localhost:4200</c> call the API on <c>http://localhost:5096</c>.
    /// Deployed environments do not use this: there the frontend origin is
    /// allowed at the App Service edge instead, so both the registration here
    /// and the middleware in Program.cs are gated on the Development environment.
    /// </summary>
    /// <remarks>
    /// Origins come from configuration rather than a constant so a clone serving
    /// the UI on another port needs a settings edit, not a code change. The list
    /// is the only thing that opens the policy up: an absent or empty
    /// <c>Cors:AllowedOrigins</c> admits nothing, and there is no wildcard
    /// fallback. Credentials stay off because the API authenticates with a
    /// bearer token in a header, not a cookie.
    /// </remarks>
    public static IServiceCollection AddLocalDevelopmentCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];

        return services.AddCors(options =>
            options.AddPolicy(LocalDevelopmentPolicyName, policy => policy
                .WithOrigins(allowedOrigins)
                .WithMethods(
                    HttpMethods.Get,
                    HttpMethods.Post,
                    HttpMethods.Put,
                    HttpMethods.Delete,
                    HttpMethods.Options)
                .WithHeaders(
                    HeaderNames.Authorization,
                    // Required for both JSON bodies and the multipart upload path.
                    HeaderNames.ContentType,
                    CorrelationIdMiddleware.HeaderName)
                // Without this the browser can read the response but not the
                // correlation id the middleware stamps on it, which is the one
                // value a reviewer needs to find the request in the logs.
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));
    }
}
