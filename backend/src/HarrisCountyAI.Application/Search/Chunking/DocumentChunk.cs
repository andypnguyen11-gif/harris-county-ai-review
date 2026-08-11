namespace HarrisCountyAI.Application.Search.Chunking;

public sealed record DocumentChunk
{
    public required string ChunkId { get; init; }   // "{documentId:N}-{sequence:D4}"
    public required Guid DocumentId { get; init; }
    public required int Sequence { get; init; }     // 0-based order within the document
    public required string Text { get; init; }
    public string? Title { get; init; }             // parent document title
    public string? Section { get; init; }           // nearest heading / section label
    public int? PageNumber { get; init; }
}
