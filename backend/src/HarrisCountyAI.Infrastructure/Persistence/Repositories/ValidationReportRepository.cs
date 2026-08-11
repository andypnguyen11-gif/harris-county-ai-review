using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.Infrastructure.Persistence.Repositories;

public sealed class ValidationReportRepository : IValidationReportRepository
{
    private readonly ApplicationDbContext _context;

    public ValidationReportRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ValidationReport?> GetByIdAsync(Guid caseId, Guid reportId, CancellationToken cancellationToken = default) =>
        _context.ValidationReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CaseId == caseId, cancellationToken);

    public Task<ValidationReport?> GetLatestByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default) =>
        _context.ValidationReports
            .Where(r => r.CaseId == caseId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(ValidationReport report, CancellationToken cancellationToken = default) =>
        await _context.ValidationReports.AddAsync(report, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
