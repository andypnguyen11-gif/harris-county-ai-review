using HarrisCountyAI.Application.Cases;
using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.Infrastructure.Persistence.Repositories;

public sealed class CaseRepository : ICaseRepository
{
    private readonly ApplicationDbContext _context;

    public CaseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Case?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Cases.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Case>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Cases
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<string?> GetLatestCaseNumberAsync(string prefix, CancellationToken cancellationToken = default) =>
        _context.Cases
            .Where(c => c.CaseNumber.StartsWith(prefix))
            .OrderByDescending(c => c.CaseNumber)
            .Select(c => (string?)c.CaseNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(Case @case, CancellationToken cancellationToken = default) =>
        await _context.Cases.AddAsync(@case, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
