namespace HarrisCountyAI.Application.Search.Retrieval;

/// <summary>
/// Which of the two strictly separated corpora a retrieval targets — and,
/// downstream, which corpus a retrieved passage or citation came from. The
/// corpora share one physical index but are never blended: county queries
/// filter to knowledge-base chunks, case queries filter to one case's
/// uploaded documents (see docs/architecture/rag-architecture.md).
/// </summary>
public enum SourceType
{
    /// <summary>The curated Harris County reference corpus.</summary>
    County,

    /// <summary>The documents uploaded to one specific case.</summary>
    Case,
}
