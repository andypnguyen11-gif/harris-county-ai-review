using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// <see cref="IEmbeddingBatchClient"/> backed by the Azure OpenAI SDK. This class contains
/// no batching or retry logic of its own; <see cref="AzureEmbeddingService"/> owns both.
/// </summary>
public sealed class AzureOpenAIEmbeddingBatchClient : IEmbeddingBatchClient
{
    private readonly EmbeddingClient _client;

    public AzureOpenAIEmbeddingBatchClient(IOptions<EmbeddingOptions> options)
    {
        var settings = options.Value;

        var clientOptions = new AzureOpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            // AzureEmbeddingService owns retries; disable the SDK's built-in retry policy
            // so transient failures are not retried twice.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

        _client = new AzureOpenAIClient(
                new Uri(settings.Endpoint),
                new ApiKeyCredential(settings.ApiKey),
                clientOptions)
            .GetEmbeddingClient(settings.Deployment);
    }

    public async Task<EmbeddingBatchResponse> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        OpenAIEmbeddingCollection embeddings = await _client
            .GenerateEmbeddingsAsync(inputs, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var vectors = embeddings
            .OrderBy(embedding => embedding.Index)
            .Select(embedding => embedding.ToFloats().ToArray())
            .ToArray();

        return new EmbeddingBatchResponse(vectors, embeddings.Model);
    }
}
