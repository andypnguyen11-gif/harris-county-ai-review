using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.KnowledgeBase;

/// <summary>Persistence abstraction for knowledge documents so the application layer stays free of EF Core.</summary>
public interface IKnowledgeDocumentRepository
{
    Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns knowledge documents, newest first. Deactivated documents are
    /// excluded unless <paramref name="includeDeactivated"/> is <c>true</c>.
    /// </summary>
    Task<IReadOnlyList<KnowledgeDocument>> GetAllAsync(bool includeDeactivated = false, CancellationToken cancellationToken = default);

    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
