using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.Validation;

/// <summary>Persistence abstraction for validation reports.</summary>
public interface IValidationReportRepository
{
    /// <summary>Returns the report with the given id, scoped to the case it belongs to.</summary>
    Task<ValidationReport?> GetByIdAsync(Guid caseId, Guid reportId, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent report for the case, or null when the case has never been validated.</summary>
    Task<ValidationReport?> GetLatestByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default);

    Task AddAsync(ValidationReport report, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
