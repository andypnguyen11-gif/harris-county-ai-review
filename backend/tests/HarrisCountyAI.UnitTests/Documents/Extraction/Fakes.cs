using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Documents.Extraction;

/// <summary>In-memory <see cref="IDocumentRepository"/> that records the status persisted at each save.</summary>
public sealed class FakeDocumentRepository : IDocumentRepository
{
    private readonly Dictionary<Guid, Document> _documents = [];

    public List<DocumentProcessingStatus> SavedStatuses { get; } = [];

    public int SaveChangesCallCount { get; private set; }

    public void Add(Document document) => _documents[document.Id] = document;

    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.TryGetValue(id, out var document) ? document : null);

    public Task<Document?> GetByIdAsync(Guid caseId, Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.TryGetValue(documentId, out var document) && document.CaseId == caseId ? document : null);

    public Task<IReadOnlyList<Document>> GetByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>(_documents.Values
            .Where(d => d.CaseId == caseId)
            .OrderBy(d => d.CreatedAt)
            .ToList());

    public Task AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        _documents[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        SavedStatuses.AddRange(_documents.Values.Select(d => d.ProcessingStatus));
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IDocumentStorageService"/> serving canned blob content.</summary>
public sealed class FakeDocumentStorageService : IDocumentStorageService
{
    private readonly Dictionary<(DocumentStorageContainer, string), byte[]> _blobs = [];

    public Exception? DownloadException { get; set; }

    public List<string> DownloadedPaths { get; } = [];

    public void AddBlob(DocumentStorageContainer container, string blobPath, byte[] content) =>
        _blobs[(container, blobPath)] = content;

    public Task<string> UploadAsync(DocumentStorageContainer container, string blobPath, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        _blobs[(container, blobPath)] = buffer.ToArray();
        return Task.FromResult(blobPath);
    }

    public Task<Stream> DownloadAsync(DocumentStorageContainer container, string blobPath, CancellationToken cancellationToken = default)
    {
        DownloadedPaths.Add(blobPath);

        if (DownloadException is not null)
        {
            throw DownloadException;
        }

        if (!_blobs.TryGetValue((container, blobPath), out var content))
        {
            throw new FileNotFoundException($"Blob '{blobPath}' was not found.", blobPath);
        }

        return Task.FromResult<Stream>(new MemoryStream(content));
    }

    public Task<bool> DeleteAsync(DocumentStorageContainer container, string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.Remove((container, blobPath)));

    public Task<bool> ExistsAsync(DocumentStorageContainer container, string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.ContainsKey((container, blobPath)));
}

/// <summary>Canned <see cref="IDocumentExtractionService"/>.</summary>
public sealed class FakeDocumentExtractionService : IDocumentExtractionService
{
    public Exception? ExtractException { get; set; }

    public Guid? LastDocumentId { get; private set; }

    public Task<ExtractedDocument> ExtractAsync(Guid documentId, Stream content, CancellationToken cancellationToken)
    {
        LastDocumentId = documentId;

        if (ExtractException is not null)
        {
            throw ExtractException;
        }

        return Task.FromResult(new ExtractedDocument
        {
            DocumentId = documentId,
            Pages = [new ExtractedPage { PageNumber = 1, Text = "Fake page text" }],
            KeyValuePairs = [new ExtractedField { Key = "Applicant Name:", Value = "Jane Doe", Confidence = 0.9, PageNumber = 1 }],
            RawText = "Fake page text",
            ModelId = "fake-model",
            ExtractedAt = DateTime.UtcNow,
        });
    }
}

/// <summary>In-memory <see cref="INormalizedDocumentRepository"/>.</summary>
public sealed class FakeNormalizedDocumentRepository : INormalizedDocumentRepository
{
    public List<NormalizedDocument> Added { get; } = [];

    public int SaveChangesCallCount { get; private set; }

    public Task<NormalizedDocument?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Added.OrderByDescending(d => d.CreatedAt).FirstOrDefault(d => d.DocumentId == documentId));

    public Task<IReadOnlyList<NormalizedDocument>> GetLatestByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NormalizedDocument>>(Added
            .Where(d => d.CaseId == caseId)
            .GroupBy(d => d.DocumentId)
            .Select(group => group.OrderByDescending(d => d.CreatedAt).First())
            .OrderBy(d => d.CreatedAt)
            .ToList());

    public Task AddAsync(NormalizedDocument normalizedDocument, CancellationToken cancellationToken = default)
    {
        Added.Add(normalizedDocument);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
