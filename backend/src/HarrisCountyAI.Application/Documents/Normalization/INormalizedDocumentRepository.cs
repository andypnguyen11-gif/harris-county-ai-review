using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.Documents.Normalization;

/// <summary>Persistence abstraction for normalized document snapshots.</summary>
public interface INormalizedDocumentRepository
{
    Task<NormalizedDocument?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task AddAsync(NormalizedDocument normalizedDocument, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
