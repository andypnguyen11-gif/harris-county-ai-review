using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Cases.UpdateCase;

/// <summary>Partial update: only non-null members are applied.</summary>
public sealed record UpdateCaseCommand(string? Name, CaseStatus? Status);
