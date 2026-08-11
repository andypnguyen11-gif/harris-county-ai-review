using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Validation;

/// <summary>
/// Runs a set of validation rules against a case and aggregates their results. A rule that
/// throws never aborts the run: its failure is logged and reported as an
/// <see cref="ValidationStatus.UnableToDetermine"/> result so the remaining rules still execute.
/// </summary>
public class DocumentValidationService
{
    private readonly ILogger<DocumentValidationService> _logger;

    public DocumentValidationService(ILogger<DocumentValidationService>? logger = null)
    {
        _logger = logger ?? NullLogger<DocumentValidationService>.Instance;
    }

    public async Task<IReadOnlyList<ValidationResult>> ValidateAsync(
        ValidationContext context,
        IEnumerable<IValidationRule> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);

        var results = new List<ValidationResult>();
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(await rule.ValidateAsync(context, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Validation rule {RuleName} failed for case {CaseId}.", rule.Name, context.CaseId);
                results.Add(new ValidationResult
                {
                    Requirement = rule.Name,
                    Status = ValidationStatus.UnableToDetermine,
                    Message = $"Rule '{rule.Name}' failed to execute: {exception.Message}",
                    ValidationType = ValidationType.Deterministic,
                    RuleName = rule.Name,
                });
            }
        }

        return results;
    }
}
