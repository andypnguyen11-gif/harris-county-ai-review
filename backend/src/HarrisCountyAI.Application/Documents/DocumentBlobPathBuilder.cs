using System.Text.RegularExpressions;

namespace HarrisCountyAI.Application.Documents;

/// <summary>
/// Builds deterministic, storage-safe blob paths for document content.
/// Case documents are stored under <c>{caseId}/{documentId}_{safeFileName}</c>.
/// </summary>
public static partial class DocumentBlobPathBuilder
{
    private const string FallbackFileName = "file";

    /// <summary>
    /// Builds the blob path for a case document:
    /// <c>{caseId}/{documentId}_{safeFileName}</c>.
    /// </summary>
    public static string ForCaseDocument(Guid caseId, Guid documentId, string fileName)
        => $"{caseId:D}/{documentId:D}_{SanitizeFileName(fileName)}";

    /// <summary>
    /// Reduces an arbitrary client-supplied file name to a storage-safe name:
    /// directory components are stripped, characters outside
    /// <c>[A-Za-z0-9._-]</c> are replaced with <c>-</c>, runs of dots and
    /// dashes are collapsed (so <c>..</c> can never survive), and
    /// leading/trailing separators are trimmed.
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Strip any directory components (handles both separator styles).
        var name = fileName.Trim();
        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..];
        }

        name = UnsafeCharacters().Replace(name, "-");
        name = DotWithSurroundingDashes().Replace(name, ".");
        name = DotRuns().Replace(name, ".");
        name = DashRuns().Replace(name, "-");
        name = name.Trim('.', '-', '_');

        return name.Length == 0 ? FallbackFileName : name;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeCharacters();

    [GeneratedRegex(@"-*\.-*")]
    private static partial Regex DotWithSurroundingDashes();

    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex DotRuns();

    [GeneratedRegex("-{2,}")]
    private static partial Regex DashRuns();
}
