namespace HarrisCountyAI.Application.KnowledgeBase.Ingestion;

/// <summary>Outcome of a single ingestion run over a knowledge document.</summary>
public enum IngestionStatus
{
    /// <summary>The document was extracted, chunked, embedded, and indexed.</summary>
    Succeeded,

    /// <summary>
    /// A pipeline stage failed; the document is marked
    /// <see cref="Domain.Enums.KnowledgeDocumentIngestionStatus.Failed"/> and
    /// may be reprocessed.
    /// </summary>
    Failed,
}
