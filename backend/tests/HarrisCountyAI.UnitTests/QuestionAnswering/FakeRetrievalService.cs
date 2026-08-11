using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.UnitTests.QuestionAnswering;

/// <summary>
/// In-memory <see cref="IRetrievalService"/> that records requests and serves
/// canned chunks.
/// </summary>
public sealed class FakeRetrievalService : IRetrievalService
{
    public List<RetrievalRequest> Requests { get; } = [];

    /// <summary>The most recent request, or null if none were made.</summary>
    public RetrievalRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    /// <summary>Chunks returned by <see cref="RetrieveAsync"/> for scopes with no scope-specific setup.</summary>
    public IReadOnlyList<RetrievedChunk> ChunksToReturn { get; set; } = [];

    /// <summary>
    /// Chunks returned per requested scope, so a dual-source caller's two
    /// retrievals can be answered with distinct evidence. Falls back to
    /// <see cref="ChunksToReturn"/> for scopes not present here.
    /// </summary>
    public Dictionary<SourceType, IReadOnlyList<RetrievedChunk>> ChunksByScope { get; } = [];

    /// <summary>When set, <see cref="RetrieveAsync"/> throws this instead of returning chunks.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(
            ChunksByScope.TryGetValue(request.Scope, out var scoped) ? scoped : ChunksToReturn);
    }

    /// <summary>The single request made with the given scope; fails when there is not exactly one.</summary>
    public RetrievalRequest RequestFor(SourceType scope) =>
        Requests.Single(request => request.Scope == scope);

    /// <summary>Builds a corpus chunk with citation-relevant metadata.</summary>
    public static RetrievedChunk Chunk(
        string chunkId = "0f8fad5bd9cb469fa165408319b0e0d9-0000",
        string? documentIdValue = null,
        string text = "A completed application form is required.",
        string title = "Floodplain Regulations",
        string? section = "Section 4.2",
        int? page = 17,
        string? sourceUrl = "https://www.hcfcd.org/regulations") => new()
        {
            ChunkId = chunkId,
            DocumentId = documentIdValue is null
                ? Guid.Parse("0f8fad5b-d9cb-469f-a165-408319b0e0d9")
                : Guid.Parse(documentIdValue),
            Text = text,
            Title = title,
            Section = section,
            Page = page,
            SourceUrl = sourceUrl,
            Score = 0.9,
        };
}
