using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.UnitTests.Search.Reranking;

/// <summary>
/// In-memory <see cref="IRerankingService"/> that records every request and,
/// by default, reverses the candidates (a visibly different order) before
/// trimming to TopN.
/// </summary>
public sealed class FakeRerankingService : IRerankingService
{
    public List<RerankingRequest> ReceivedRequests { get; } = [];

    /// <summary>The most recent request, or null if none were received.</summary>
    public RerankingRequest? LastRequest => ReceivedRequests.Count == 0 ? null : ReceivedRequests[^1];

    /// <summary>When set, returned instead of the default reversed-and-trimmed candidates.</summary>
    public IReadOnlyList<RetrievedChunk>? ResultToReturn { get; set; }

    public Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        RerankingRequest request,
        CancellationToken cancellationToken = default)
    {
        ReceivedRequests.Add(request);

        IReadOnlyList<RetrievedChunk> result = ResultToReturn
            ?? request.Candidates.Reverse().Take(request.TopN).ToList();
        return Task.FromResult(result);
    }
}
