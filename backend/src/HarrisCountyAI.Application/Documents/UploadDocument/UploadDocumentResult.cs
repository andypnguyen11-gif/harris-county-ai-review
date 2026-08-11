namespace HarrisCountyAI.Application.Documents.UploadDocument;

public enum UploadDocumentOutcome
{
    Uploaded,
    CaseNotFound,
    InvalidFile,
}

/// <summary>
/// Outcome of an upload attempt. Exactly one of <see cref="Document"/>
/// (success) or <see cref="Errors"/> (invalid file) is populated.
/// </summary>
public sealed class UploadDocumentResult
{
    private UploadDocumentResult(UploadDocumentOutcome outcome, DocumentDto? document, IReadOnlyList<string> errors)
    {
        Outcome = outcome;
        Document = document;
        Errors = errors;
    }

    public UploadDocumentOutcome Outcome { get; }

    /// <summary>The stored document; only set when <see cref="Outcome"/> is <see cref="UploadDocumentOutcome.Uploaded"/>.</summary>
    public DocumentDto? Document { get; }

    /// <summary>Validation errors; only populated when <see cref="Outcome"/> is <see cref="UploadDocumentOutcome.InvalidFile"/>.</summary>
    public IReadOnlyList<string> Errors { get; }

    public static UploadDocumentResult Uploaded(DocumentDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(UploadDocumentOutcome.Uploaded, document, []);
    }

    public static UploadDocumentResult CaseNotFound() =>
        new(UploadDocumentOutcome.CaseNotFound, null, []);

    public static UploadDocumentResult InvalidFile(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? throw new ArgumentException("An invalid-file result requires at least one error.", nameof(errors))
            : new(UploadDocumentOutcome.InvalidFile, null, errors);
    }
}
