namespace HarrisCountyAI.Application.Validation.GetValidationReport;

public sealed class GetValidationReportHandler
{
    private readonly IValidationReportRepository _reportRepository;

    public GetValidationReportHandler(IValidationReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    /// <summary>Returns the report, or null when it does not exist or belongs to another case.</summary>
    public async Task<ValidationReportDto?> HandleAsync(Guid caseId, Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _reportRepository.GetByIdAsync(caseId, reportId, cancellationToken);
        return report is null ? null : ValidationReportDto.FromEntity(report);
    }
}
