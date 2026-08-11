using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>
/// The authored set of requirements for one workflow. Requirements come from
/// here — from the county regulations, transcribed into code — and never from a
/// model or from an applicant's document.
/// </summary>
public interface IRequirementCatalog
{
    /// <summary>The workflow this catalog describes.</summary>
    WorkflowType WorkflowType { get; }

    /// <summary>The requirements, in the order they should be reported.</summary>
    IReadOnlyList<Requirement> GetRequirements();
}
