using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.Infrastructure.Persistence.Repositories;

public sealed class NormalizedDocumentRepository : INormalizedDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public NormalizedDocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<NormalizedDocument?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        _context.NormalizedDocuments
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);

    public async Task AddAsync(NormalizedDocument normalizedDocument, CancellationToken cancellationToken = default) =>
        await _context.NormalizedDocuments.AddAsync(normalizedDocument, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
