using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.Documents;

/// <summary>Persistence abstraction for documents so the application layer stays free of EF Core.</summary>
public interface IDocumentRepository
{
    /// <summary>Returns the document only when it exists and belongs to <paramref name="caseId"/>.</summary>
    Task<Document?> GetByIdAsync(Guid caseId, Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Returns the document by id alone, for the processing pipeline where the owning case is not yet known.</summary>
    Task<Document?> GetByIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Returns all documents for the case, oldest first.</summary>
    Task<IReadOnlyList<Document>> GetByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default);

    Task AddAsync(Document document, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
