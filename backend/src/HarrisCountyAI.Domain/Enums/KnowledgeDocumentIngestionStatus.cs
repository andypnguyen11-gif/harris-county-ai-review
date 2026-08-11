namespace HarrisCountyAI.Domain.Enums;

/// <summary>Lifecycle of a knowledge document within the ingestion pipeline.</summary>
public enum KnowledgeDocumentIngestionStatus
{
    /// <summary>Stored in blob storage; not yet picked up by the ingestion pipeline.</summary>
    Uploaded,

    /// <summary>The ingestion pipeline is extracting, chunking, and indexing the document.</summary>
    Processing,

    /// <summary>Successfully indexed into the reference corpus.</summary>
    Ingested,

    /// <summary>The last ingestion attempt failed; the document can be reprocessed.</summary>
    Failed,

    /// <summary>Soft-deleted; excluded from the active corpus and from retrieval.</summary>
    Deactivated,
}
