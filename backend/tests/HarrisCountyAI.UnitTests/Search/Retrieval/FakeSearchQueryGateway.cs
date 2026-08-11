using HarrisCountyAI.Infrastructure.Azure.Search;

namespace HarrisCountyAI.UnitTests.Search.Retrieval;

/// <summary>
/// In-memory <see cref="ISearchQueryGateway"/> that records every query for
/// assertion and serves canned hits.
/// </summary>
public sealed class FakeSearchQueryGateway : ISearchQueryGateway
{
    public List<ChunkSearchQuery> ExecutedQueries { get; } = [];

    /// <summary>The most recent query, or null if none were executed.</summary>
    public ChunkSearchQuery? LastQuery => ExecutedQueries.Count == 0 ? null : ExecutedQueries[^1];

    /// <summary>Hits returned by <see cref="SearchAsync"/>.</summary>
    public IReadOnlyList<ChunkSearchHit> HitsToReturn { get; set; } = [];

    /// <summary>When set, <see cref="SearchAsync"/> throws instead of returning hits.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public Task<IReadOnlyList<ChunkSearchHit>> SearchAsync(
        ChunkSearchQuery query,
        CancellationToken cancellationToken)
    {
        ExecutedQueries.Add(query);
        return ExceptionToThrow is null
            ? Task.FromResult(HitsToReturn)
            : Task.FromException<IReadOnlyList<ChunkSearchHit>>(ExceptionToThrow);
    }
}
