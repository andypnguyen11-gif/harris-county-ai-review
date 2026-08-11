using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HarrisCountyAI.Api.Authentication;

/// <summary>
/// Issues HMAC-SHA256 signed JWTs for the allow-listed development users defined in the
/// "Authentication:LocalDevelopment" configuration section. Claims mirror the shape of
/// Entra ID tokens ("name", "preferred_username", "roles") so downstream authorization
/// is identical in both modes.
/// </summary>
public sealed class DevTokenService : IDevTokenService
{
    private readonly LocalDevelopmentAuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;

    public DevTokenService(IOptions<ApiAuthenticationOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value.LocalDevelopment;
        _timeProvider = timeProvider;
    }

    public DevToken? IssueToken(string username)
    {
        var user = _options.Users.FirstOrDefault(
            u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.TokenLifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Username,
                ["preferred_username"] = user.Username,
                ["name"] = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                ["roles"] = user.Roles.ToArray(),
            },
        };

        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        return new DevToken(
            accessToken,
            new DateTimeOffset(expires, TimeSpan.Zero),
            user.Username,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            user.Roles.ToArray());
    }
}
