using HarrisCountyAI.Application.Validation.GetValidationReport;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.Application;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

public class GetValidationReportHandlersTests
{
    private readonly FakeCaseRepository _caseRepository = new();
    private readonly FakeValidationReportRepository _reports = new();

    private async Task<Case> AddCaseAsync()
    {
        var @case = Case.Create("HC-2026-0001", "Creek Bend Development", WorkflowType.FloodplainDevelopmentPermit);
        await _caseRepository.AddAsync(@case);
        return @case;
    }

    private async Task<ValidationReport> AddReportAsync(Guid caseId, DateTime? createdAt = null)
    {
        var report = new ValidationReport
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Items =
            [
                new ValidationReportItem
                {
                    Id = Guid.NewGuid(),
                    Order = 0,
                    RuleName = "RequiredDocumentRule(Site plan)",
                    Requirement = "Site plan",
                    ValidationType = ValidationType.Deterministic,
                    Status = ValidationStatus.Complete,
                    Message = "A SitePlan document is present.",
                },
            ],
        };

        await _reports.AddAsync(report);
        return report;
    }

    [Fact]
    public async Task GetLatest_Returns_Null_For_Unknown_Case()
    {
        var dto = await new GetLatestValidationReportHandler(_caseRepository, _reports).HandleAsync(Guid.NewGuid());

        Assert.Null(dto);
    }

    [Fact]
    public async Task GetLatest_Returns_Null_When_Case_Was_Never_Validated()
    {
        var @case = await AddCaseAsync();

        var dto = await new GetLatestValidationReportHandler(_caseRepository, _reports).HandleAsync(@case.Id);

        Assert.Null(dto);
    }

    [Fact]
    public async Task GetLatest_Returns_The_Most_Recent_Report()
    {
        var @case = await AddCaseAsync();
        await AddReportAsync(@case.Id, DateTime.UtcNow.AddMinutes(-5));
        var latest = await AddReportAsync(@case.Id, DateTime.UtcNow);

        var dto = await new GetLatestValidationReportHandler(_caseRepository, _reports).HandleAsync(@case.Id);

        Assert.NotNull(dto);
        Assert.Equal(latest.Id, dto.Id);
        var item = Assert.Single(dto.Items);
        Assert.Equal("Site plan", item.Requirement);
        Assert.Equal(ValidationStatus.Complete, item.Status);
    }

    [Fact]
    public async Task GetById_Returns_The_Report()
    {
        var @case = await AddCaseAsync();
        var report = await AddReportAsync(@case.Id);

        var dto = await new GetValidationReportHandler(_reports).HandleAsync(@case.Id, report.Id);

        Assert.NotNull(dto);
        Assert.Equal(report.Id, dto.Id);
        Assert.Equal(@case.Id, dto.CaseId);
    }

    [Fact]
    public async Task GetById_Returns_Null_For_Unknown_Report()
    {
        var @case = await AddCaseAsync();

        var dto = await new GetValidationReportHandler(_reports).HandleAsync(@case.Id, Guid.NewGuid());

        Assert.Null(dto);
    }

    [Fact]
    public async Task GetById_Returns_Null_When_Report_Belongs_To_Another_Case()
    {
        var @case = await AddCaseAsync();
        var foreignReport = await AddReportAsync(Guid.NewGuid());

        var dto = await new GetValidationReportHandler(_reports).HandleAsync(@case.Id, foreignReport.Id);

        Assert.Null(dto);
    }
}
