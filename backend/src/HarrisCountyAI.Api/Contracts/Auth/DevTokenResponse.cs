namespace HarrisCountyAI.Api.Contracts.Auth;

public sealed record DevTokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles);
