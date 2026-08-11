using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation.Workflows;

/// <summary>Maps a review workflow to the configured set of validation rules it runs.</summary>
public interface IWorkflowDefinition
{
    WorkflowType WorkflowType { get; }

    /// <summary>Builds the ordered rule set for this workflow. Each call returns fresh rule instances.</summary>
    IReadOnlyList<IValidationRule> BuildRules();
}
