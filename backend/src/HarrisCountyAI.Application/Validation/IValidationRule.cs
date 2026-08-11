using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation;

/// <summary>A single deterministic check evaluated against a case's normalized documents.</summary>
public interface IValidationRule
{
    /// <summary>Identifies this configured rule instance, e.g. "RequiredFieldRule(Owner name)".</summary>
    string Name { get; }

    Task<ValidationResult> ValidateAsync(ValidationContext context, CancellationToken cancellationToken);
}
