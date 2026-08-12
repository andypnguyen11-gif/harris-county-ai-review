using HarrisCountyAI.Application.Evaluation.Generation;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// The recorder is what makes unsupported-claim detection honest: it captures
/// the evidence the answer was actually built from, rather than what a second
/// retrieval would return.
/// </summary>
public sealed class RecordingRetrievalServiceTests
{
    private static RetrievalRequest Request(string query = "q") => new() { Query = query };

    [Fact]
    public async Task Requests_Pass_Through_Unchanged_And_Results_Are_Returned_Verbatim()
    {
        var inner = new FakeRetrievalService { ChunksToReturn = [FakeRetrievalService.Chunk()] };
        var recorder = new RecordingRetrievalService(inner);

        var chunks = await recorder.RetrieveAsync(Request("how high"));

        Assert.Equal(inner.ChunksToReturn, chunks);
        Assert.Equal("how high", Assert.Single(inner.Requests).Query);
    }

    [Fact]
    public async Task Drain_Returns_What_Was_Retrieved_And_Clears_The_Buffer()
    {
        var inner = new FakeRetrievalService { ChunksToReturn = [FakeRetrievalService.Chunk()] };
        var recorder = new RecordingRetrievalService(inner);

        await recorder.RetrieveAsync(Request());

        Assert.Single(recorder.Drain());
        Assert.Empty(recorder.Drain());
    }

    [Fact]
    public async Task Several_Retrievals_For_One_Answer_Are_Pooled()
    {
        // Dual-source answering retrieves the corpus and the case separately;
        // both are evidence for the same answer.
        var inner = new FakeRetrievalService
        {
            ChunksToReturn = [FakeRetrievalService.Chunk(chunkId: "a"), FakeRetrievalService.Chunk(chunkId: "b")],
        };
        var recorder = new RecordingRetrievalService(inner);

        await recorder.RetrieveAsync(Request());
        await recorder.RetrieveAsync(Request());

        Assert.Equal(4, recorder.Drain().Count);
    }

    [Fact]
    public async Task A_Failing_Retrieval_Records_Nothing_And_Propagates()
    {
        var inner = new FakeRetrievalService { ExceptionToThrow = new InvalidOperationException("down") };
        var recorder = new RecordingRetrievalService(inner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RetrieveAsync(Request()));

        Assert.Empty(recorder.Drain());
    }

    [Fact]
    public void An_Inner_Service_Is_Required()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingRetrievalService(null!));
    }
}
