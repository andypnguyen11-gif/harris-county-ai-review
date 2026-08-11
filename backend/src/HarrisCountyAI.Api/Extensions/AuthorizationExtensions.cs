using HarrisCountyAI.Api.Authorization;
using HarrisCountyAI.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace HarrisCountyAI.Api.Extensions;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Registers role-based authorization policies and a fallback policy that requires
    /// an authenticated user on every endpoint that does not opt out explicitly with
    /// <see cref="AllowAnonymousAttribute"/> (health, OpenAPI in development, dev-token).
    /// Roles come exclusively from validated token claims; client-supplied role data is
    /// never trusted.
    /// </summary>
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.RequireReviewer, policy =>
                policy.RequireRole(ApplicationRoles.Reviewer, ApplicationRoles.Administrator))
            .AddPolicy(AuthorizationPolicies.RequireAdministrator, policy =>
                policy.RequireRole(ApplicationRoles.Administrator))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
