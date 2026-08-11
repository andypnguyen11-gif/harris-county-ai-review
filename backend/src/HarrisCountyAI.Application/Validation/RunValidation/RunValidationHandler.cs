using HarrisCountyAI.Application.Cases;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Application.Validation.Workflows;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation.RunValidation;

/// <summary>
/// Runs the case's workflow rule set against its extracted documents and persists
/// the outcome as a new <see cref="Domain.Entities.ValidationReport"/>. Each run
/// appends a report; earlier reports are kept as history.
/// </summary>
public sealed class RunValidationHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly INormalizedDocumentRepository _normalizedDocumentRepository;
    private readonly IValidationReportRepository _reportRepository;
    private readonly DocumentValidationService _validationService;
    private readonly IReadOnlyDictionary<WorkflowType, IWorkflowDefinition> _workflows;

    public RunValidationHandler(
        ICaseRepository caseRepository,
        INormalizedDocumentRepository normalizedDocumentRepository,
        IValidationReportRepository reportRepository,
        DocumentValidationService validationService,
        IEnumerable<IWorkflowDefinition> workflows)
    {
        _caseRepository = caseRepository;
        _normalizedDocumentRepository = normalizedDocumentRepository;
        _reportRepository = reportRepository;
        _validationService = validationService;
        _workflows = workflows.ToDictionary(workflow => workflow.WorkflowType);
    }

    /// <summary>Validates the case and returns the persisted report, or null when the case does not exist.</summary>
    public async Task<ValidationReportDto?> HandleAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, cancellationToken);
        if (@case is null)
        {
            return null;
        }

        if (!_workflows.TryGetValue(@case.WorkflowType, out var workflow))
        {
            throw new InvalidOperationException(
                $"No workflow definition is registered for workflow type '{@case.WorkflowType}'.");
        }

        var documents = await _normalizedDocumentRepository.GetLatestByCaseIdAsync(caseId, cancellationToken);
        var context = new ValidationContext(caseId, @case.WorkflowType, documents);
        var results = await _validationService.ValidateAsync(context, workflow.BuildRules(), cancellationToken);

        var report = ValidationReportFactory.Create(caseId, @case.WorkflowType, results, documents);
        await _reportRepository.AddAsync(report, cancellationToken);
        await _reportRepository.SaveChangesAsync(cancellationToken);

        return ValidationReportDto.FromEntity(report);
    }
}
