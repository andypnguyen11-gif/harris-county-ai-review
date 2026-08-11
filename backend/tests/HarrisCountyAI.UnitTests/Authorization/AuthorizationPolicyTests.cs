using System.Security.Claims;
using HarrisCountyAI.Api.Authorization;
using HarrisCountyAI.Api.Extensions;
using HarrisCountyAI.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.UnitTests.Authorization;

public class AuthorizationPolicyTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddApiAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var claims = new List<Claim> { new("name", "test.user") };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, authenticationType: "Test", nameType: "name", roleType: "roles"));
    }

    private static ClaimsPrincipal AnonymousPrincipal() => new(new ClaimsIdentity());

    [Fact]
    public void Role_Constants_Match_The_Values_Used_In_Configuration()
    {
        Assert.Equal("Reviewer", ApplicationRoles.Reviewer);
        Assert.Equal("Administrator", ApplicationRoles.Administrator);
        Assert.Equal("RequireReviewer", AuthorizationPolicies.RequireReviewer);
        Assert.Equal("RequireAdministrator", AuthorizationPolicies.RequireAdministrator);
    }

    [Fact]
    public async Task RequireReviewer_Policy_Allows_Reviewer_And_Administrator_Roles()
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policyProvider.GetPolicyAsync(AuthorizationPolicies.RequireReviewer);

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(
            [ApplicationRoles.Reviewer, ApplicationRoles.Administrator],
            requirement.AllowedRoles.ToArray());
    }

    [Fact]
    public async Task RequireAdministrator_Policy_Allows_Only_Administrator_Role()
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policyProvider.GetPolicyAsync(AuthorizationPolicies.RequireAdministrator);

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal([ApplicationRoles.Administrator], requirement.AllowedRoles.ToArray());
    }

    [Fact]
    public async Task Fallback_Policy_Requires_An_Authenticated_User()
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Single(fallback.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());
    }

    [Theory]
    [InlineData("Reviewer", true)]
    [InlineData("Administrator", true)]
    [InlineData("SomeOtherRole", false)]
    public async Task RequireReviewer_Evaluates_Roles(string role, bool expected)
    {
        await using var provider = BuildProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            PrincipalWithRoles(role), null, AuthorizationPolicies.RequireReviewer);

        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Reviewer", false)]
    public async Task RequireAdministrator_Evaluates_Roles(string role, bool expected)
    {
        await using var provider = BuildProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            PrincipalWithRoles(role), null, AuthorizationPolicies.RequireAdministrator);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Authenticated_User_Without_Roles_Fails_Role_Policies_But_Passes_Fallback()
    {
        await using var provider = BuildProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var principal = PrincipalWithRoles();

        var reviewer = await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.RequireReviewer);
        var fallback = await authorization.AuthorizeAsync(
            principal, null, (await policyProvider.GetFallbackPolicyAsync())!);

        Assert.False(reviewer.Succeeded);
        Assert.True(fallback.Succeeded);
    }

    [Fact]
    public async Task Anonymous_User_Fails_Fallback_And_Role_Policies()
    {
        await using var provider = BuildProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var principal = AnonymousPrincipal();

        var reviewer = await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.RequireReviewer);
        var fallback = await authorization.AuthorizeAsync(
            principal, null, (await policyProvider.GetFallbackPolicyAsync())!);

        Assert.False(reviewer.Succeeded);
        Assert.False(fallback.Succeeded);
    }
}
