namespace HarrisCountyAI.Api.Authentication;

/// <summary>Supported values for the "Authentication:Mode" configuration key.</summary>
public static class AuthenticationModes
{
    /// <summary>Local JWTs signed with a symmetric key; dev-token endpoint enabled.</summary>
    public const string LocalDevelopment = "LocalDevelopment";

    /// <summary>Microsoft Entra ID via authority metadata; dev-token endpoint disabled.</summary>
    public const string EntraId = "EntraId";
}

/// <summary>
/// Bound to the "Authentication" configuration section. Switching <see cref="Mode"/>
/// from <see cref="AuthenticationModes.LocalDevelopment"/> to
/// <see cref="AuthenticationModes.EntraId"/> swaps token validation from the local
/// symmetric signing key to the Entra ID authority without code changes.
/// </summary>
public sealed class ApiAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Mode { get; set; } = string.Empty;

    public LocalDevelopmentAuthenticationOptions LocalDevelopment { get; set; } = new();

    public EntraIdAuthenticationOptions EntraId { get; set; } = new();
}

/// <summary>Settings used only when Mode is "LocalDevelopment".</summary>
public sealed class LocalDevelopmentAuthenticationOptions
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Symmetric HMAC-SHA256 signing key. Local development only; never a production secret.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>Allow-listed development users the dev-token endpoint may issue tokens for.</summary>
    public List<DevelopmentUser> Users { get; set; } = [];
}

/// <summary>An allow-listed development identity defined in configuration.</summary>
public sealed class DevelopmentUser
{
    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];
}

/// <summary>Settings used only when Mode is "EntraId".</summary>
public sealed class EntraIdAuthenticationOptions
{
    /// <summary>Token authority, e.g. https://login.microsoftonline.com/{tenantId}/v2.0.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Expected audience (the API's application ID URI or client ID).</summary>
    public string Audience { get; set; } = string.Empty;
}
