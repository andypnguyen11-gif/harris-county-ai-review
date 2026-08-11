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

    public async Task<IReadOnlyList<NormalizedDocument>> GetLatestByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        // Reprocessing a document appends a fresh snapshot; the latest snapshot
        // per source document is the current one. The per-document grouping is
        // done in memory because a case only ever has a handful of snapshots.
        var documents = await _context.NormalizedDocuments
            .Where(d => d.CaseId == caseId)
            .ToListAsync(cancellationToken);

        return documents
            .GroupBy(d => d.DocumentId)
            .Select(group => group.OrderByDescending(d => d.CreatedAt).First())
            .OrderBy(d => d.CreatedAt)
            .ToList();
    }

    public async Task AddAsync(NormalizedDocument normalizedDocument, CancellationToken cancellationToken = default) =>
        await _context.NormalizedDocuments.AddAsync(normalizedDocument, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
