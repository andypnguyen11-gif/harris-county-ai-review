using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Cases;

/// <summary>Wire representation of a case. Enums are serialized as strings.</summary>
public sealed record CaseDto(
    Guid Id,
    string CaseNumber,
    string Name,
    WorkflowType WorkflowType,
    CaseStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static CaseDto FromEntity(Case @case) => new(
        @case.Id,
        @case.CaseNumber,
        @case.Name,
        @case.WorkflowType,
        @case.Status,
        @case.CreatedAt,
        @case.UpdatedAt);
}
