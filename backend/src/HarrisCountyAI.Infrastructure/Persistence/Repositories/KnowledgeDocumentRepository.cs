using HarrisCountyAI.Application.KnowledgeBase;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public KnowledgeDocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.KnowledgeDocuments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeDocument>> GetAllAsync(
        bool includeDeactivated = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<KnowledgeDocument> query = _context.KnowledgeDocuments;

        if (!includeDeactivated)
        {
            query = query.Where(d => d.IngestionStatus != KnowledgeDocumentIngestionStatus.Deactivated);
        }

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken = default) =>
        await _context.KnowledgeDocuments.AddAsync(document, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
