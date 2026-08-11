using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Documents.UploadDocument;

/// <summary>
/// Request to upload a file to a case. <paramref name="Content"/> is read once
/// during the upload; the caller owns the stream's lifetime.
/// </summary>
public sealed record UploadDocumentCommand(
    Guid CaseId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    Stream Content,
    DocumentType DocumentType);
