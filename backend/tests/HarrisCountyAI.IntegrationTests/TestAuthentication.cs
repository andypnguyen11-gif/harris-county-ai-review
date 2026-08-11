using System.Net.Http.Headers;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HarrisCountyAI.IntegrationTests;

/// <summary>
/// Authentication values the <see cref="TestApplicationFactory"/> configures the
/// application with, plus helpers to mint tokens for tests without going through
/// the dev-token endpoint.
/// </summary>
public static class TestAuthentication
{
    public const string Issuer = "HarrisCountyAI.Tests";
    public const string Audience = "HarrisCountyAI.Api.Tests";
    public const string SigningKey = "integration-test-signing-key-0123456789-not-a-secret";

    public const string ReviewerUsername = "dev.reviewer";
    public const string AdministratorUsername = "dev.admin";

    /// <summary>Creates a signed JWT accepted by the test-configured application.</summary>
    public static string CreateToken(
        string username = ReviewerUsername,
        string[]? roles = null,
        string signingKey = SigningKey,
        string issuer = Issuer,
        string audience = Audience,
        TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;
        var effectiveLifetime = lifetime ?? TimeSpan.FromMinutes(10);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.Add(effectiveLifetime < TimeSpan.Zero ? effectiveLifetime * 2 : TimeSpan.Zero),
            NotBefore = now.Add(effectiveLifetime < TimeSpan.Zero ? effectiveLifetime * 2 : TimeSpan.Zero),
            Expires = now.Add(effectiveLifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = username,
                ["preferred_username"] = username,
                ["name"] = username,
                ["roles"] = roles ?? ["Reviewer"],
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Sets a bearer token on the client for all subsequent requests.</summary>
    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
