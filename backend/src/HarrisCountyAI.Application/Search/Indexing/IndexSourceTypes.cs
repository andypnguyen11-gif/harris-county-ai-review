namespace HarrisCountyAI.Application.Search.Indexing;

/// <summary>
/// The two — and only two — origins a chunk in the search index can have.
/// Case-specific uploaded documents and the Harris County reference corpus
/// share one physical index but must always be filterable apart; every
/// indexed chunk carries exactly one of these values.
/// </summary>
public static class IndexSourceTypes
{
    /// <summary>Chunk from the curated Harris County reference corpus.</summary>
    public const string KnowledgeBase = "KnowledgeBase";

    /// <summary>Chunk from a document an applicant uploaded to a case.</summary>
    public const string CaseDocument = "CaseDocument";

    /// <summary>Whether <paramref name="value"/> is a recognized source type.</summary>
    public static bool IsValid(string value) => value is KnowledgeBase or CaseDocument;
}
