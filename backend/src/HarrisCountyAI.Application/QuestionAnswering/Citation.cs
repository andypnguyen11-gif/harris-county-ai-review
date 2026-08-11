namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// One corpus source an answer is grounded in, resolved from the numbered
/// source the model cited back to the retrieved chunk it refers to.
/// </summary>
public sealed record Citation
{
    /// <summary>The source number the answer cites (1-based, as shown to the model).</summary>
    public required int Number { get; init; }

    /// <summary>Search-index key of the cited chunk.</summary>
    public required string ChunkId { get; init; }

    /// <summary>Identifier of the knowledge document the chunk came from.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Title of the source document.</summary>
    public required string Title { get; init; }

    /// <summary>Section heading the cited passage was taken from, if known.</summary>
    public string? Section { get; init; }

    /// <summary>Page the cited passage starts on, if known.</summary>
    public int? Page { get; init; }

    /// <summary>Public URL of the source document, if one exists.</summary>
    public string? SourceUrl { get; init; }
}
