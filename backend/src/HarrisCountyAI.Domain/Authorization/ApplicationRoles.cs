namespace HarrisCountyAI.Domain.Authorization;

/// <summary>
/// The application's role names. Role claims in validated tokens (local dev JWTs now,
/// Entra ID app roles later) must use these exact values.
/// </summary>
public static class ApplicationRoles
{
    /// <summary>Performs case work: cases, documents, validation, and question answering.</summary>
    public const string Reviewer = "Reviewer";

    /// <summary>Manages the knowledge base and administrative surface; also has full reviewer access.</summary>
    public const string Administrator = "Administrator";
}
