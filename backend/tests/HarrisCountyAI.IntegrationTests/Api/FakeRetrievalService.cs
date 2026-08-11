using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// In-memory <see cref="IRetrievalService"/> for API tests: records requests
/// and serves canned chunks so no search service or embedding model is needed.
/// </summary>
public sealed class FakeRetrievalService : IRetrievalService
{
    public List<RetrievalRequest> Requests { get; } = [];

    /// <summary>The most recent request, or null if none were made.</summary>
    public RetrievalRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    /// <summary>Chunks returned by <see cref="RetrieveAsync"/>.</summary>
    public IReadOnlyList<RetrievedChunk> ChunksToReturn { get; set; } = [];

    /// <summary>When set, <see cref="RetrieveAsync"/> throws this instead of returning chunks.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(ChunksToReturn);
    }

    /// <summary>Builds a fully populated chunk for response assertions.</summary>
    public static RetrievedChunk Chunk(
        string chunkId = "0f8fad5bd9cb469fa165408319b0e0d9-0000",
        string text = "A completed application form is required.",
        string title = "Floodplain Regulations",
        double score = 0.9) => new()
        {
            ChunkId = chunkId,
            DocumentId = Guid.Parse("0f8fad5b-d9cb-469f-a165-408319b0e0d9"),
            Text = text,
            Title = title,
            Section = "Section 4.2",
            Page = 17,
            Department = "Engineering",
            PermitType = "FloodplainDevelopmentPermit",
            DocumentType = "Regulation",
            EffectiveDate = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero),
            SourceUrl = "https://www.hcfcd.org/regulations",
            Score = score,
        };
}
