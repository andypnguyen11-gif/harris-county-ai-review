using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>A file uploaded to a case, stored in blob storage and processed by the extraction pipeline.</summary>
public class Document
{
    private Document(Guid id, Guid caseId, string fileName, string blobPath, DocumentType documentType, DocumentProcessingStatus processingStatus, DateTime createdAt)
    {
        Id = id;
        CaseId = caseId;
        FileName = fileName;
        BlobPath = blobPath;
        DocumentType = documentType;
        ProcessingStatus = processingStatus;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid CaseId { get; private set; }

    public string FileName { get; private set; }

    /// <summary>Path of the stored file within blob storage.</summary>
    public string BlobPath { get; private set; }

    public DocumentType DocumentType { get; private set; }

    public DocumentProcessingStatus ProcessingStatus { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static Document Create(Guid caseId, string fileName, string blobPath, DocumentType documentType)
        => Create(Guid.NewGuid(), caseId, fileName, blobPath, documentType);

    /// <summary>
    /// Creates a document with a caller-supplied id, for flows that need the id
    /// before construction (e.g. to build the blob path the file is uploaded to).
    /// </summary>
    public static Document Create(Guid id, Guid caseId, string fileName, string blobPath, DocumentType documentType)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(id));
        }

        if (caseId == Guid.Empty)
        {
            throw new ArgumentException("Case id is required.", nameof(caseId));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(blobPath))
        {
            throw new ArgumentException("Blob path is required.", nameof(blobPath));
        }

        return new Document(id, caseId, fileName.Trim(), blobPath.Trim(), documentType, DocumentProcessingStatus.Pending, DateTime.UtcNow);
    }

    public void SetProcessingStatus(DocumentProcessingStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown document processing status.");
        }

        ProcessingStatus = status;
    }
}
