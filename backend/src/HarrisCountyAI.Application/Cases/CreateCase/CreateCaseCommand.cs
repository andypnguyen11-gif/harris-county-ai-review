using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Cases.CreateCase;

public sealed record CreateCaseCommand(string Name, WorkflowType WorkflowType);
