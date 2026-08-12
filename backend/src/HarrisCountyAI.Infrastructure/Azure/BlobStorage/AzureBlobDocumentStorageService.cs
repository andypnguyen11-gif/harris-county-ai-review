using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.BlobStorage;

/// <summary>
/// <see cref="IDocumentStorageService"/> backed by Azure Blob Storage.
/// Containers are created on first use; each
/// <see cref="DocumentStorageContainer"/> maps to a container name from
/// <see cref="BlobStorageOptions"/>.
/// </summary>
/// <remarks>
/// Storage failures are separated into two kinds. A blob that is not there is
/// reported as a <see cref="FileNotFoundException"/>, because "the file is
/// gone" is an answer callers can act on — the document viewer turns it into a
/// 404 with its own explanation. Everything else (a refused connection, a
/// throttled account, a 5xx) becomes an
/// <see cref="ExternalServiceUnavailableException"/>, which the API reports as
/// storage being down rather than as the caller's mistake.
/// </remarks>
public sealed class AzureBlobDocumentStorageService : IDocumentStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobStorageOptions _options;
    private readonly ILogger<AzureBlobDocumentStorageService> _logger;
    private readonly ConcurrentDictionary<string, Task> _containerInitializations = new();

    public AzureBlobDocumentStorageService(
        BlobServiceClient blobServiceClient,
        IOptions<BlobStorageOptions> options,
        ILogger<AzureBlobDocumentStorageService>? logger = null)
    {
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
        _logger = logger ?? NullLogger<AzureBlobDocumentStorageService>.Instance;
    }

    public Task<string> UploadAsync(
        DocumentStorageContainer container,
        string blobPath,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);

        return Execute("upload", async token =>
        {
            var blobClient = await GetBlobClientAsync(container, blobPath, token);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            };

            await blobClient.UploadAsync(content, uploadOptions, token);
            return blobPath;
        }, cancellationToken);
    }

    public Task<Stream> DownloadAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        return Execute("download", async token =>
        {
            var blobClient = await GetBlobClientAsync(container, blobPath, token);

            try
            {
                var response = await blobClient.DownloadStreamingAsync(cancellationToken: token);
                return response.Value.Content;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                throw new FileNotFoundException(
                    $"Blob '{blobPath}' was not found in container '{GetContainerName(container)}'.",
                    blobPath,
                    ex);
            }
        }, cancellationToken);
    }

    public Task<bool> DeleteAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        return Execute("delete", async token =>
        {
            var blobClient = await GetBlobClientAsync(container, blobPath, token);
            var response = await blobClient.DeleteIfExistsAsync(cancellationToken: token);
            return response.Value;
        }, cancellationToken);
    }

    public Task<bool> ExistsAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        return Execute("exists", async token =>
        {
            var blobClient = await GetBlobClientAsync(container, blobPath, token);
            var response = await blobClient.ExistsAsync(token);
            return response.Value;
        }, cancellationToken);
    }

    private Task<T> Execute<T>(string operation, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => AzureOperationExecutor.ExecuteAsync(
            ExternalServiceNames.DocumentStorage, operation, action, cancellationToken, _logger);

    private string GetContainerName(DocumentStorageContainer container) => container switch
    {
        DocumentStorageContainer.CaseDocuments => _options.CaseDocumentsContainerName,
        DocumentStorageContainer.KnowledgeBase => _options.KnowledgeBaseContainerName,
        _ => throw new ArgumentOutOfRangeException(nameof(container), container, "Unknown storage container."),
    };

    private async Task<BlobClient> GetBlobClientAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken)
    {
        var containerName = GetContainerName(container);
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

        // Create the container once per process; retry on the next call if a
        // previous attempt failed.
        var initialization = _containerInitializations.GetOrAdd(
            containerName,
            _ => containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken));

        try
        {
            await initialization;
        }
        catch
        {
            _containerInitializations.TryRemove(containerName, out _);
            throw;
        }

        return containerClient.GetBlobClient(blobPath);
    }
}
