using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>
/// A curated Harris County reference document (permit form, regulation,
/// checklist, ...) stored in blob storage and ingested into the reference
/// corpus. Kept strictly separate from case-specific uploaded documents.
/// </summary>
public class KnowledgeDocument
{
    private KnowledgeDocument(
        Guid id,
        string title,
        string fileName,
        string blobPath,
        string department,
        string documentType,
        string permitType,
        string? version,
        DateOnly? effectiveDate,
        string? sourceUrl,
        KnowledgeDocumentIngestionStatus ingestionStatus,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        Title = title;
        FileName = fileName;
        BlobPath = blobPath;
        Department = department;
        DocumentType = documentType;
        PermitType = permitType;
        Version = version;
        EffectiveDate = effectiveDate;
        SourceUrl = sourceUrl;
        IngestionStatus = ingestionStatus;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Human-readable document title, e.g. "Floodplain Development Permit Application".</summary>
    public string Title { get; private set; }

    public string FileName { get; private set; }

    /// <summary>Path of the stored file within the knowledge-base blob container.</summary>
    public string BlobPath { get; private set; }

    /// <summary>Owning county department, e.g. "Engineering".</summary>
    public string Department { get; private set; }

    /// <summary>Kind of reference material, e.g. "PermitForm", "Regulation", "Checklist".</summary>
    public string DocumentType { get; private set; }

    /// <summary>Permit workflow the document applies to, e.g. "FloodplainDevelopment".</summary>
    public string PermitType { get; private set; }

    /// <summary>Document revision identifier, when the source material carries one.</summary>
    public string? Version { get; private set; }

    /// <summary>Date the document's requirements take effect.</summary>
    public DateOnly? EffectiveDate { get; private set; }

    /// <summary>Public URL the document was sourced from.</summary>
    public string? SourceUrl { get; private set; }

    public KnowledgeDocumentIngestionStatus IngestionStatus { get; private set; }

    /// <summary>When the document was last successfully ingested into the corpus.</summary>
    public DateTime? IngestionDate { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a knowledge document in the <see cref="KnowledgeDocumentIngestionStatus.Uploaded"/>
    /// state. The caller supplies <paramref name="id"/> because the blob path
    /// embeds the document id and the file is stored before the entity is persisted.
    /// </summary>
    public static KnowledgeDocument Create(
        Guid id,
        string title,
        string fileName,
        string blobPath,
        string department,
        string documentType,
        string permitType,
        string? version = null,
        DateOnly? effectiveDate = null,
        string? sourceUrl = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(id));
        }

        var now = DateTime.UtcNow;
        return new KnowledgeDocument(
            id,
            Required(title, nameof(title), "Title"),
            Required(fileName, nameof(fileName), "File name"),
            Required(blobPath, nameof(blobPath), "Blob path"),
            Required(department, nameof(department), "Department"),
            Required(documentType, nameof(documentType), "Document type"),
            Required(permitType, nameof(permitType), "Permit type"),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
            effectiveDate,
            string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl.Trim(),
            KnowledgeDocumentIngestionStatus.Uploaded,
            now,
            now);
    }

    /// <summary>Marks the document as picked up by the ingestion pipeline.</summary>
    public void MarkProcessing()
    {
        EnsureNotDeactivated();

        if (IngestionStatus == KnowledgeDocumentIngestionStatus.Processing)
        {
            throw new InvalidOperationException("The document is already being processed.");
        }

        IngestionStatus = KnowledgeDocumentIngestionStatus.Processing;
        Touch();
    }

    /// <summary>Records a successful ingestion into the reference corpus.</summary>
    public void MarkIngested()
    {
        EnsureProcessing(nameof(MarkIngested));

        IngestionStatus = KnowledgeDocumentIngestionStatus.Ingested;
        IngestionDate = DateTime.UtcNow;
        Touch();
    }

    /// <summary>Records a failed ingestion attempt; the document may be reprocessed.</summary>
    public void MarkFailed()
    {
        EnsureProcessing(nameof(MarkFailed));

        IngestionStatus = KnowledgeDocumentIngestionStatus.Failed;
        Touch();
    }

    /// <summary>
    /// Soft-deletes the document: it is excluded from the active corpus but
    /// its record and file are retained. Idempotent.
    /// </summary>
    public void Deactivate()
    {
        if (IngestionStatus == KnowledgeDocumentIngestionStatus.Deactivated)
        {
            return;
        }

        IngestionStatus = KnowledgeDocumentIngestionStatus.Deactivated;
        Touch();
    }

    private void EnsureNotDeactivated()
    {
        if (IngestionStatus == KnowledgeDocumentIngestionStatus.Deactivated)
        {
            throw new InvalidOperationException("A deactivated document cannot change ingestion state.");
        }
    }

    private void EnsureProcessing(string operation)
    {
        if (IngestionStatus != KnowledgeDocumentIngestionStatus.Processing)
        {
            throw new InvalidOperationException(
                $"{operation} requires status {KnowledgeDocumentIngestionStatus.Processing}; the document is {IngestionStatus}.");
        }
    }

    private static string Required(string value, string paramName, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.", paramName);
        }

        return value.Trim();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
