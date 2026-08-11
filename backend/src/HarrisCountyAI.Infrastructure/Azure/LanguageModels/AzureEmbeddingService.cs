using System.ClientModel;
using HarrisCountyAI.Application.Search.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Generates embeddings through Azure OpenAI. Splits inputs into batches of at most
/// <see cref="EmbeddingOptions.MaxBatchSize"/>, retries transient failures (HTTP 429 and 5xx)
/// with exponential backoff plus jitter, and maps each returned vector back to the index of
/// the input it was generated for.
/// </summary>
public sealed class AzureEmbeddingService : IEmbeddingService
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxJitter = TimeSpan.FromMilliseconds(250);

    private readonly IEmbeddingBatchClient _client;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<AzureEmbeddingService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public AzureEmbeddingService(
        IEmbeddingBatchClient client,
        IOptions<EmbeddingOptions> options,
        ILogger<AzureEmbeddingService> logger)
        : this(client, options, logger, Task.Delay)
    {
    }

    /// <summary>
    /// Test seam: lets unit tests replace the backoff delay so retry behavior can be
    /// verified without real waits.
    /// </summary>
    internal AzureEmbeddingService(
        IEmbeddingBatchClient client,
        IOptions<EmbeddingOptions> options,
        ILogger<AzureEmbeddingService> logger,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _delayAsync = delayAsync;
    }

    public async Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count == 0)
        {
            return [];
        }

        var results = new EmbeddingResult[inputs.Count];
        var maxBatchSize = Math.Max(1, _options.MaxBatchSize);
        string? model = null;

        for (var offset = 0; offset < inputs.Count; offset += maxBatchSize)
        {
            var batchSize = Math.Min(maxBatchSize, inputs.Count - offset);
            var batch = new string[batchSize];
            for (var i = 0; i < batchSize; i++)
            {
                batch[i] = inputs[offset + i];
            }

            var response = await SendWithRetryAsync(batch, offset, ct).ConfigureAwait(false);

            if (response.Vectors.Count != batchSize)
            {
                throw new EmbeddingServiceException(
                    $"Embedding batch for inputs {offset}..{offset + batchSize - 1} returned " +
                    $"{response.Vectors.Count} vectors for {batchSize} inputs.");
            }

            model = response.Model;
            for (var i = 0; i < batchSize; i++)
            {
                results[offset + i] = new EmbeddingResult(response.Vectors[i], offset + i, response.Model);
            }
        }

        // Input text is intentionally never logged at Information level; only counts and
        // model/deployment metadata.
        _logger.LogInformation(
            "Generated {EmbeddingCount} embeddings using deployment {Deployment} (model {Model}).",
            inputs.Count,
            _options.Deployment,
            model);

        return results;
    }

    private async Task<EmbeddingBatchResponse> SendWithRetryAsync(
        IReadOnlyList<string> batch,
        int batchStartIndex,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _client.GenerateEmbeddingsAsync(batch, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                var delay = ComputeBackoff(attempt);
                _logger.LogWarning(
                    ex,
                    "Transient embedding failure for batch of {BatchSize} inputs starting at input index " +
                    "{InputIndex} (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}.",
                    batch.Count,
                    batchStartIndex,
                    attempt + 1,
                    MaxRetries + 1,
                    delay);

                await _delayAsync(delay, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var lastIndex = batchStartIndex + batch.Count - 1;
                _logger.LogError(
                    ex,
                    "Embedding request failed for batch of {BatchSize} inputs starting at input index " +
                    "{InputIndex} after {Attempts} attempt(s).",
                    batch.Count,
                    batchStartIndex,
                    attempt + 1);

                var message = IsTransient(ex)
                    ? $"Embedding request for inputs {batchStartIndex}..{lastIndex} failed after " +
                      $"{MaxRetries + 1} attempts; the service kept returning transient errors."
                    : $"Embedding request for inputs {batchStartIndex}..{lastIndex} failed with a " +
                      "non-retryable error.";

                throw new EmbeddingServiceException(message, ex);
            }
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is ClientResultException clientResultException
        && (clientResultException.Status == 429 || clientResultException.Status >= 500);

    private static TimeSpan ComputeBackoff(int attempt)
    {
        // 500ms, 1s, 2s exponential base plus up to 250ms of jitter to avoid retry stampedes.
        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * MaxJitter.TotalMilliseconds);
        return exponential + jitter;
    }
}
