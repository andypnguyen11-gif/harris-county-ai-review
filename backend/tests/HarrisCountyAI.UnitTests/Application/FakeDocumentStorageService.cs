using HarrisCountyAI.Application.Documents;

namespace HarrisCountyAI.UnitTests.Application;

/// <summary>In-memory IDocumentStorageService test double that records uploads.</summary>
internal sealed class FakeDocumentStorageService : IDocumentStorageService
{
    private readonly Dictionary<(DocumentStorageContainer Container, string BlobPath), (string ContentType, byte[] Content)> _blobs = [];

    public sealed record UploadRecord(DocumentStorageContainer Container, string BlobPath, string ContentType, byte[] Content);

    public List<UploadRecord> Uploads { get; } = [];

    /// <summary>When set, UploadAsync throws to simulate a storage outage.</summary>
    public Exception? UploadFailure { get; set; }

    public async Task<string> UploadAsync(
        DocumentStorageContainer container,
        string blobPath,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (UploadFailure is not null)
        {
            throw UploadFailure;
        }

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        _blobs[(container, blobPath)] = (contentType, bytes);
        Uploads.Add(new UploadRecord(container, blobPath, contentType, bytes));
        return blobPath;
    }

    public Task<Stream> DownloadAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default) =>
        _blobs.TryGetValue((container, blobPath), out var blob)
            ? Task.FromResult<Stream>(new MemoryStream(blob.Content))
            : throw new FileNotFoundException($"Blob '{blobPath}' was not found.", blobPath);

    public Task<bool> DeleteAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.Remove((container, blobPath)));

    public Task<bool> ExistsAsync(
        DocumentStorageContainer container,
        string blobPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.ContainsKey((container, blobPath)));
}
