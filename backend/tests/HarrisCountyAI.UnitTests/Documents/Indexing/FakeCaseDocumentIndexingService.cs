using HarrisCountyAI.Application.Documents.Indexing;

namespace HarrisCountyAI.UnitTests.Documents.Indexing;

/// <summary>In-memory <see cref="ICaseDocumentIndexingService"/> that records calls.</summary>
public sealed class FakeCaseDocumentIndexingService : ICaseDocumentIndexingService
{
    public List<Guid> IndexedDocumentIds { get; } = [];

    public List<Guid> RemovedDocumentIds { get; } = [];

    /// <summary>When set, <see cref="IndexAsync"/> throws this instead of indexing.</summary>
    public Exception? IndexException { get; set; }

    public Task<CaseDocumentIndexingResult?> IndexAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (IndexException is not null)
        {
            throw IndexException;
        }

        IndexedDocumentIds.Add(documentId);
        return Task.FromResult<CaseDocumentIndexingResult?>(new CaseDocumentIndexingResult
        {
            DocumentId = documentId,
            CaseId = Guid.NewGuid(),
            ChunkCount = 1,
        });
    }

    public Task RemoveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        RemovedDocumentIds.Add(documentId);
        return Task.CompletedTask;
    }
}
