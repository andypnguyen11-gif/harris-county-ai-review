namespace HarrisCountyAI.Application.Search.Chunking;

/// <summary>
/// Extracted text of a document to be chunked. Pages are processed in order;
/// page numbers, when known, are carried onto the resulting chunks.
/// </summary>
public sealed record ChunkingRequest
{
    public required Guid DocumentId { get; init; }

    /// <summary>Parent document title, propagated to every chunk.</summary>
    public string? Title { get; init; }

    public required IReadOnlyList<ChunkingPage> Pages { get; init; }

    /// <summary>Convenience factory for text without page structure.</summary>
    public static ChunkingRequest FromText(Guid documentId, string? title, string text) => new()
    {
        DocumentId = documentId,
        Title = title,
        Pages = [new ChunkingPage { Text = text }],
    };
}

/// <summary>A single page (or unpaged span) of extracted document text.</summary>
public sealed record ChunkingPage
{
    public int? PageNumber { get; init; }

    public required string Text { get; init; }
}
