namespace HarrisCountyAI.Api.Authentication;

/// <summary>
/// Issues signed JWTs for allow-listed development users. Registered only when
/// "Authentication:Mode" is "LocalDevelopment".
/// </summary>
public interface IDevTokenService
{
    /// <summary>
    /// Issues a token for the given username, or returns null when the username
    /// is not in the configured allow list.
    /// </summary>
    DevToken? IssueToken(string username);
}

/// <summary>A development token issued for an allow-listed user.</summary>
public sealed record DevToken(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles);
