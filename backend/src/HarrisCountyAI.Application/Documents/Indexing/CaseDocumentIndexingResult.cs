namespace HarrisCountyAI.Application.Documents.Indexing;

/// <summary>Outcome of indexing one case document.</summary>
public sealed record CaseDocumentIndexingResult
{
    /// <summary>The indexed document.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>The case the document belongs to.</summary>
    public required Guid CaseId { get; init; }

    /// <summary>Number of chunks written to the search index.</summary>
    public required int ChunkCount { get; init; }
}
