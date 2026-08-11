using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.Infrastructure.Persistence.Repositories;

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public DocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Document?> GetByIdAsync(Guid caseId, Guid documentId, CancellationToken cancellationToken = default) =>
        _context.Documents.SingleOrDefaultAsync(
            d => d.Id == documentId && d.CaseId == caseId, cancellationToken);

    public async Task<IReadOnlyList<Document>> GetByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default) =>
        await _context.Documents
            .Where(d => d.CaseId == caseId)
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Document document, CancellationToken cancellationToken = default) =>
        await _context.Documents.AddAsync(document, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
