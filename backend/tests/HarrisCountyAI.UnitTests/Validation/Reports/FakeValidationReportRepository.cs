using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

/// <summary>In-memory <see cref="IValidationReportRepository"/> test double.</summary>
internal sealed class FakeValidationReportRepository : IValidationReportRepository
{
    private readonly List<ValidationReport> _reports = [];

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyList<ValidationReport> Reports => _reports;

    public Task<ValidationReport?> GetByIdAsync(Guid caseId, Guid reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_reports.SingleOrDefault(r => r.Id == reportId && r.CaseId == caseId));

    public Task<ValidationReport?> GetLatestByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_reports
            .Where(r => r.CaseId == caseId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault());

    public Task AddAsync(ValidationReport report, CancellationToken cancellationToken = default)
    {
        _reports.Add(report);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
