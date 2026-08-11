namespace HarrisCountyAI.Api.Contracts.Cases;

public sealed record CreateCaseRequest(string? Name, string? WorkflowType);
