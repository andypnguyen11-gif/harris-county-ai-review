using System.Text;
using HarrisCountyAI.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace HarrisCountyAI.Api.Extensions;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Configures JWT bearer authentication driven entirely by the "Authentication"
    /// configuration section. "LocalDevelopment" mode validates locally issued tokens
    /// against a symmetric signing key and enables the dev-token endpoint; "EntraId"
    /// mode validates against the configured authority's metadata instead. Switching
    /// modes is a configuration change only.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(ApiAuthenticationOptions.SectionName);
        var options = section.Get<ApiAuthenticationOptions>()
            ?? throw new InvalidOperationException(
                $"Missing '{ApiAuthenticationOptions.SectionName}' configuration section.");

        services.Configure<ApiAuthenticationOptions>(section);
        services.TryAddSingleton(TimeProvider.System);

        var authenticationBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        switch (options.Mode)
        {
            case AuthenticationModes.LocalDevelopment:
                ValidateLocalDevelopmentOptions(options.LocalDevelopment);
                services.AddSingleton<IDevTokenService, DevTokenService>();
                authenticationBuilder.AddJwtBearer(jwt =>
                {
                    jwt.MapInboundClaims = false;
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = options.LocalDevelopment.Issuer,
                        ValidateAudience = true,
                        ValidAudience = options.LocalDevelopment.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(options.LocalDevelopment.SigningKey)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,
                        NameClaimType = "name",
                        RoleClaimType = "roles",
                    };
                });
                break;

            case AuthenticationModes.EntraId:
                ValidateEntraIdOptions(options.EntraId);
                authenticationBuilder.AddJwtBearer(jwt =>
                {
                    jwt.MapInboundClaims = false;
                    jwt.Authority = options.EntraId.Authority;
                    jwt.Audience = options.EntraId.Audience;
                    jwt.TokenValidationParameters.NameClaimType = "name";
                    jwt.TokenValidationParameters.RoleClaimType = "roles";
                });
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported '{ApiAuthenticationOptions.SectionName}:Mode' value " +
                    $"'{options.Mode}'. Expected '{AuthenticationModes.LocalDevelopment}' " +
                    $"or '{AuthenticationModes.EntraId}'.");
        }

        return services;
    }

    private static void ValidateLocalDevelopmentOptions(LocalDevelopmentAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException(
                "'Authentication:LocalDevelopment:Issuer' is required in LocalDevelopment mode.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException(
                "'Authentication:LocalDevelopment:Audience' is required in LocalDevelopment mode.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "'Authentication:LocalDevelopment:SigningKey' must be at least 32 characters.");
        }

        if (options.TokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException(
                "'Authentication:LocalDevelopment:TokenLifetimeMinutes' must be positive.");
        }
    }

    private static void ValidateEntraIdOptions(EntraIdAuthenticationOptions options)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority)
            || authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "'Authentication:EntraId:Authority' must be an absolute https URL in EntraId mode.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException(
                "'Authentication:EntraId:Audience' is required in EntraId mode.");
        }
    }
}
