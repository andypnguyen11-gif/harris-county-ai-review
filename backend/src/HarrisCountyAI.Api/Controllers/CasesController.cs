using HarrisCountyAI.Api.Contracts.Cases;
using HarrisCountyAI.Application.Cases;
using HarrisCountyAI.Application.Cases.CreateCase;
using HarrisCountyAI.Application.Cases.GetCase;
using HarrisCountyAI.Application.Cases.GetCases;
using HarrisCountyAI.Application.Cases.UpdateCase;
using HarrisCountyAI.Api.Authorization;
using HarrisCountyAI.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HarrisCountyAI.Api.Controllers;

[ApiController]
[Route("api/cases")]
[Authorize(Policy = AuthorizationPolicies.RequireReviewer)]
public class CasesController : ControllerBase
{
    private readonly CreateCaseHandler _createCase;
    private readonly GetCaseHandler _getCase;
    private readonly GetCasesHandler _getCases;
    private readonly UpdateCaseHandler _updateCase;

    public CasesController(
        CreateCaseHandler createCase,
        GetCaseHandler getCase,
        GetCasesHandler getCases,
        UpdateCaseHandler updateCase)
    {
        _createCase = createCase;
        _getCase = getCase;
        _getCases = getCases;
        _updateCase = updateCase;
    }

    [HttpPost]
    [ProducesResponseType<CaseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateCaseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError("name", "Name is required.");
        }

        var workflowType = ParseEnum<WorkflowType>(request.WorkflowType, "workflowType");

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var command = new CreateCaseCommand(request.Name!, workflowType!.Value);
        var created = await _createCase.HandleAsync(command, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CaseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var cases = await _getCases.HandleAsync(cancellationToken);
        return Ok(cases);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CaseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var @case = await _getCase.HandleAsync(id, cancellationToken);
        return @case is null ? NotFound() : Ok(@case);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CaseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateCaseRequest request, CancellationToken cancellationToken)
    {
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError("name", "Name must not be empty.");
        }

        CaseStatus? status = null;
        if (request.Status is not null)
        {
            status = ParseEnum<CaseStatus>(request.Status, "status");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var command = new UpdateCaseCommand(request.Name, status);
        var updated = await _updateCase.HandleAsync(id, command, cancellationToken);

        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Parses an enum from its string name, recording a model error when invalid.</summary>
    private TEnum? ParseEnum<TEnum>(string? value, string fieldName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        ModelState.AddModelError(
            fieldName,
            $"Value must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        return null;
    }
}
