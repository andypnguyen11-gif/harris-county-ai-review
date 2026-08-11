using HarrisCountyAI.Application.Cases;

namespace HarrisCountyAI.Application.Validation.GetValidationReport;

public sealed class GetLatestValidationReportHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly IValidationReportRepository _reportRepository;

    public GetLatestValidationReportHandler(ICaseRepository caseRepository, IValidationReportRepository reportRepository)
    {
        _caseRepository = caseRepository;
        _reportRepository = reportRepository;
    }

    /// <summary>Returns the case's most recent report, or null when the case does not exist or has never been validated.</summary>
    public async Task<ValidationReportDto?> HandleAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, cancellationToken);
        if (@case is null)
        {
            return null;
        }

        var report = await _reportRepository.GetLatestByCaseIdAsync(caseId, cancellationToken);
        return report is null ? null : ValidationReportDto.FromEntity(report);
    }
}
