namespace HarrisCountyAI.Api.Authorization;

/// <summary>Names of the authorization policies registered in AuthorizationExtensions.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Satisfied by the Reviewer or Administrator role. Apply to case-work endpoints.</summary>
    public const string RequireReviewer = "RequireReviewer";

    /// <summary>Satisfied only by the Administrator role. Apply to knowledge-base and admin endpoints.</summary>
    public const string RequireAdministrator = "RequireAdministrator";
}
