using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HarrisCountyAI.Application.Documents;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.BlobStorage;

/// <summary>
/// <see cref="IDocumentStorageService"/> backed by Azure Blob Storage.
/// Containers are created on first use; each
/// <see cref="DocumentStorageContainer"/> maps to a container name from
/// <see cref="BlobStorageOptions"/>.
/// </summary>
public sealed class AzureBlobDocumentStorageService : IDocumentStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobStorageOptions _options;
    private readonly ConcurrentDictionary<string, Task> _containerInitializations = new();

    public AzureBlobDocumentStorageService(
        BlobServiceClient blobServiceClient,
        IOptions<BlobStorageOptions> options)
    {
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
    }

    public async Task<string> UploadAsync(
        DocumentStorageContainer container,
        string blobPath,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);

        var blobClient = await GetBlobClientAsync(container, blobPath, cancellationToken);

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        };

        await blobClient.UploadAsync(content, uploadOptions, cancellationToken);
        return blobPath;
    }

    public async Task<Stream> DownloadAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        var blobClient = await GetBlobClientAsync(container, blobPath, cancellationToken);

        try
        {
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException(
                $"Blob '{blobPath}' was not found in container '{GetContainerName(container)}'.",
                blobPath,
                ex);
        }
    }

    public async Task<bool> DeleteAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        var blobClient = await GetBlobClientAsync(container, blobPath, cancellationToken);
        var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    public async Task<bool> ExistsAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        var blobClient = await GetBlobClientAsync(container, blobPath, cancellationToken);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }

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
