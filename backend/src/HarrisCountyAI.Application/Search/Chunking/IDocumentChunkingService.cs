namespace HarrisCountyAI.Application.Search.Chunking;

/// <summary>Turns extracted document text into ordered, searchable chunks.</summary>
public interface IDocumentChunkingService
{
    /// <summary>
    /// Splits the document into chunks, preferring section boundaries over
    /// fixed-size splits. Returns an empty list for empty or whitespace input.
    /// </summary>
    IReadOnlyList<DocumentChunk> ChunkDocument(ChunkingRequest request);
}
