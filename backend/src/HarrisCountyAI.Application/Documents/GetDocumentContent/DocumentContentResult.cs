namespace HarrisCountyAI.Application.Documents.GetDocumentContent;

/// <summary>How a request for a document's stored file concluded.</summary>
public enum DocumentContentOutcome
{
    /// <summary>The file was found and <see cref="DocumentContentResult.Content"/> is readable.</summary>
    Found,

    /// <summary>No such document exists on that case.</summary>
    DocumentNotFound,

    /// <summary>The document record exists but its stored file does not.</summary>
    FileUnavailable,
}

/// <summary>
/// A document's stored file, ready to stream to a reviewer, or an explanation
/// of why it is not available.
/// </summary>
public sealed record DocumentContentResult
{
    public required DocumentContentOutcome Outcome { get; init; }

    /// <summary>The file's bytes; non-null only when <see cref="Outcome"/> is <see cref="DocumentContentOutcome.Found"/>.</summary>
    public Stream? Content { get; init; }

    /// <summary>The original file name, for the content-disposition header.</summary>
    public string? FileName { get; init; }

    /// <summary>MIME type inferred from the file name.</summary>
    public string? ContentType { get; init; }

    public static DocumentContentResult NotFound(DocumentContentOutcome outcome) => new() { Outcome = outcome };
}
