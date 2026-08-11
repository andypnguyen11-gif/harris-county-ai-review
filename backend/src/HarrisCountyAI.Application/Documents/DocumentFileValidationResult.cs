namespace HarrisCountyAI.Application.Documents;

/// <summary>
/// Outcome of validating an uploaded file. Carries every failed rule as a
/// human-readable error reason instead of throwing.
/// </summary>
public sealed class DocumentFileValidationResult
{
    private DocumentFileValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    /// <summary>All validation errors; empty when the file is valid.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary><c>true</c> when no rule failed.</summary>
    public bool IsValid => Errors.Count == 0;

    public static DocumentFileValidationResult Success { get; } = new([]);

    public static DocumentFileValidationResult Failure(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? throw new ArgumentException("A failure result requires at least one error.", nameof(errors))
            : new DocumentFileValidationResult(errors);
    }
}
