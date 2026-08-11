namespace HarrisCountyAI.Application.Search.Chunking;

/// <summary>Tuning knobs for the chunking strategy.</summary>
public sealed record ChunkingOptions
{
    /// <summary>Maximum length of a chunk's text, in characters.</summary>
    public int MaxChunkSize { get; init; } = 2000;

    /// <summary>
    /// Number of trailing characters from the previous chunk carried into the
    /// next one when a section has to be split. No overlap is added between
    /// chunks that respect section boundaries.
    /// </summary>
    public int OverlapSize { get; init; } = 200;

    public void Validate()
    {
        if (MaxChunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxChunkSize), MaxChunkSize, "Max chunk size must be positive.");
        }

        if (OverlapSize < 0 || OverlapSize >= MaxChunkSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(OverlapSize), OverlapSize,
                "Overlap must be non-negative and smaller than the max chunk size.");
        }
    }
}
