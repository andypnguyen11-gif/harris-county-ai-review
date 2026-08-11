namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Thin query seam over the Azure AI Search SDK so retrieval services can be
/// unit tested without a live service. Implementations do no mapping or
/// business logic — they translate the query to an SDK call and return the
/// raw hits. The write-side counterpart is <see cref="ISearchIndexGateway"/>.
/// </summary>
public interface ISearchQueryGateway
{
    /// <summary>Executes <paramref name="query"/> and returns its hits, best first.</summary>
    Task<IReadOnlyList<ChunkSearchHit>> SearchAsync(ChunkSearchQuery query, CancellationToken cancellationToken);
}
