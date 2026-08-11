using System.ClientModel;
using HarrisCountyAI.Infrastructure.Azure.LanguageModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

public class AzureEmbeddingServiceTests
{
    private readonly FakeEmbeddingBatchClient _client = new();
    private readonly List<TimeSpan> _recordedDelays = [];

    private AzureEmbeddingService CreateService(int maxBatchSize = 16)
    {
        var options = Options.Create(new EmbeddingOptions
        {
            Endpoint = "https://unit-test.openai.azure.com/",
            ApiKey = "unit-test-key",
            Deployment = "text-embedding-3-small",
            MaxBatchSize = maxBatchSize,
            TimeoutSeconds = 30,
        });

        return new AzureEmbeddingService(
            _client,
            options,
            NullLogger<AzureEmbeddingService>.Instance,
            (delay, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                _recordedDelays.Add(delay);
                return Task.CompletedTask;
            });
    }

    private static IReadOnlyList<string> MakeInputs(int count) =>
        [.. Enumerable.Range(0, count).Select(i => $"chunk-{i}")];

    [Fact]
    public async Task EmbedAsync_EmptyInput_ReturnsEmptyWithoutCallingClient()
    {
        var service = CreateService();

        var results = await service.EmbedAsync([], CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(_client.ReceivedBatches);
    }

    [Fact]
    public async Task EmbedAsync_InputSmallerThanBatchSize_SendsSingleBatch()
    {
        var service = CreateService(maxBatchSize: 16);

        var results = await service.EmbedAsync(MakeInputs(5), CancellationToken.None);

        Assert.Equal(5, results.Count);
        var batch = Assert.Single(_client.ReceivedBatches);
        Assert.Equal(5, batch.Count);
    }

    [Theory]
    [InlineData(40, 16, new[] { 16, 16, 8 })]
    [InlineData(32, 16, new[] { 16, 16 })]
    [InlineData(16, 16, new[] { 16 })]
    [InlineData(17, 16, new[] { 16, 1 })]
    [InlineData(7, 3, new[] { 3, 3, 1 })]
    [InlineData(3, 1, new[] { 1, 1, 1 })]
    public async Task EmbedAsync_SplitsInputsIntoExpectedBatchSizes(
        int inputCount,
        int maxBatchSize,
        int[] expectedBatchSizes)
    {
        var service = CreateService(maxBatchSize);

        var results = await service.EmbedAsync(MakeInputs(inputCount), CancellationToken.None);

        Assert.Equal(inputCount, results.Count);
        Assert.Equal(expectedBatchSizes, _client.ReceivedBatches.Select(b => b.Count).ToArray());
    }

    [Fact]
    public async Task EmbedAsync_PreservesInputOrderAndIndicesAcrossBatches()
    {
        var service = CreateService(maxBatchSize: 4);
        var inputs = MakeInputs(10);

        var results = await service.EmbedAsync(inputs, CancellationToken.None);

        for (var i = 0; i < inputs.Count; i++)
        {
            Assert.Equal(i, results[i].InputIndex);
            // First vector component encodes the hash of the input the fake embedded,
            // proving each result maps back to its own input across batch boundaries.
            Assert.Equal(inputs[i].GetHashCode(), results[i].Vector[0]);
            // Second component encodes the batch-local position.
            Assert.Equal(i % 4, results[i].Vector[1]);
        }
    }

    [Fact]
    public async Task EmbedAsync_SetsModelMetadataOnEveryResult()
    {
        _client.Model = "text-embedding-3-small-v2";
        var service = CreateService(maxBatchSize: 2);

        var results = await service.EmbedAsync(MakeInputs(5), CancellationToken.None);

        Assert.All(results, result => Assert.Equal("text-embedding-3-small-v2", result.Model));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task EmbedAsync_TransientFailureThenSuccess_RetriesAndSucceeds(int status)
    {
        _client.EnqueueFailure(FakeClientResultExceptions.WithStatus(status));
        var service = CreateService();

        var results = await service.EmbedAsync(MakeInputs(3), CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(2, _client.ReceivedBatches.Count);
        Assert.Single(_recordedDelays);
    }

    [Fact]
    public async Task EmbedAsync_BackoffDelaysGrowExponentiallyWithBoundedJitter()
    {
        _client.EnqueueFailure(FakeClientResultExceptions.WithStatus(429));
        _client.EnqueueFailure(FakeClientResultExceptions.WithStatus(429));
        _client.EnqueueFailure(FakeClientResultExceptions.WithStatus(503));
        var service = CreateService();

        await service.EmbedAsync(MakeInputs(2), CancellationToken.None);

        Assert.Equal(3, _recordedDelays.Count);
        var expectedBases = new[] { 500d, 1000d, 2000d };
        for (var i = 0; i < 3; i++)
        {
            Assert.InRange(_recordedDelays[i].TotalMilliseconds, expectedBases[i], expectedBases[i] + 250);
        }
    }

    [Fact]
    public async Task EmbedAsync_TransientFailuresExhaustRetries_ThrowsClearException()
    {
        for (var i = 0; i < 4; i++)
        {
            _client.EnqueueFailure(FakeClientResultExceptions.WithStatus(429));
        }

        var service = CreateService();

        var exception = await Assert.ThrowsAsync<EmbeddingServiceException>(
            () => service.EmbedAsync(MakeInputs(3), CancellationToken.None));

        // One initial attempt plus three retries.
        Assert.Equal(4, _client.ReceivedBatches.Count);
        Assert.Contains("inputs 0..2", exception.Message);
        Assert.Contains("4 attempts", exception.Message);
        Assert.IsType<ClientResultException>(exception.InnerException);
    }

    [Fact]
    public async Task EmbedAsync_NonTransientFailure_DoesNotRetry()
    {
        _client.EnqueueFailure(FakeClientResultExceptions.WithStatus(400));
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<EmbeddingServiceException>(
            () => service.EmbedAsync(MakeInputs(2), CancellationToken.None));

        Assert.Single(_client.ReceivedBatches);
        Assert.Empty(_recordedDelays);
        Assert.Contains("non-retryable", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_FailureInLaterBatch_ReportsThatBatchStartIndex()
    {
        // First batch (inputs 0..3) succeeds; second batch (inputs 4..5) fails.
        _client.FailOnCallNumber = 2;
        _client.FailureFactory = () => FakeClientResultExceptions.WithStatus(400);
        var service = CreateService(maxBatchSize: 4);

        var exception = await Assert.ThrowsAsync<EmbeddingServiceException>(
            () => service.EmbedAsync(MakeInputs(6), CancellationToken.None));

        Assert.Equal(2, _client.ReceivedBatches.Count);
        Assert.Contains("inputs 4..5", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_AlreadyCancelledToken_ThrowsOperationCanceled()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EmbedAsync(MakeInputs(3), cts.Token));
    }

    [Fact]
    public async Task EmbedAsync_CancellationDuringRetryDelay_PropagatesWithoutWrapping()
    {
        _client.EnqueueFailure(FakeClientResultExceptions.WithStatus(429));
        var options = Options.Create(new EmbeddingOptions
        {
            Endpoint = "https://unit-test.openai.azure.com/",
            ApiKey = "unit-test-key",
            Deployment = "text-embedding-3-small",
            MaxBatchSize = 16,
            TimeoutSeconds = 30,
        });

        using var cts = new CancellationTokenSource();
        var service = new AzureEmbeddingService(
            _client,
            options,
            NullLogger<AzureEmbeddingService>.Instance,
            (_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EmbedAsync(MakeInputs(2), cts.Token));

        Assert.Single(_client.ReceivedBatches);
    }

    [Fact]
    public async Task EmbedAsync_VectorCountMismatch_ThrowsClearException()
    {
        _client.VectorCountOverride = 1;
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<EmbeddingServiceException>(
            () => service.EmbedAsync(MakeInputs(3), CancellationToken.None));

        Assert.Contains("returned 1 vectors for 3 inputs", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_NullInputs_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.EmbedAsync(null!, CancellationToken.None));
    }
}
