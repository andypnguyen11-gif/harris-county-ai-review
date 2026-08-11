using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.Documents.Normalization;

/// <summary>Persistence abstraction for normalized document snapshots.</summary>
public interface INormalizedDocumentRepository
{
    Task<NormalizedDocument?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Returns the latest normalized snapshot of each of the case's documents, oldest document first.</summary>
    Task<IReadOnlyList<NormalizedDocument>> GetLatestByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default);

    Task AddAsync(NormalizedDocument normalizedDocument, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
