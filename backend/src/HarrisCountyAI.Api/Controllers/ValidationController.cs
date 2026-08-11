using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Application.Validation.GetValidationReport;
using HarrisCountyAI.Api.Authorization;
using HarrisCountyAI.Application.Validation.RunValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HarrisCountyAI.Api.Controllers;

[ApiController]
[Route("api/cases/{caseId:guid}/validation")]
[Authorize(Policy = AuthorizationPolicies.RequireReviewer)]
public class ValidationController : ControllerBase
{
    private readonly RunValidationHandler _runValidation;
    private readonly GetLatestValidationReportHandler _getLatestReport;
    private readonly GetValidationReportHandler _getReport;

    public ValidationController(
        RunValidationHandler runValidation,
        GetLatestValidationReportHandler getLatestReport,
        GetValidationReportHandler getReport)
    {
        _runValidation = runValidation;
        _getLatestReport = getLatestReport;
        _getReport = getReport;
    }

    [HttpPost]
    [ProducesResponseType<ValidationReportDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Run(Guid caseId, CancellationToken cancellationToken)
    {
        var report = await _runValidation.HandleAsync(caseId, cancellationToken);
        return report is null
            ? NotFound()
            : CreatedAtAction(nameof(GetById), new { caseId, reportId = report.Id }, report);
    }

    [HttpGet]
    [ProducesResponseType<ValidationReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatest(Guid caseId, CancellationToken cancellationToken)
    {
        var report = await _getLatestReport.HandleAsync(caseId, cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("{reportId:guid}")]
    [ProducesResponseType<ValidationReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid caseId, Guid reportId, CancellationToken cancellationToken)
    {
        var report = await _getReport.HandleAsync(caseId, reportId, cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }
}
