namespace HarrisCountyAI.Application.Documents;

/// <summary>
/// Logical storage targets for document blobs. Each value maps to a dedicated
/// blob container so case-specific uploads and the Harris County reference
/// corpus are never mixed.
/// </summary>
public enum DocumentStorageContainer
{
    /// <summary>Documents uploaded for a specific case.</summary>
    CaseDocuments,

    /// <summary>The curated Harris County reference corpus.</summary>
    KnowledgeBase,
}
