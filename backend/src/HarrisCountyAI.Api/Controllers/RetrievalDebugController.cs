using HarrisCountyAI.Api.Authorization;
using HarrisCountyAI.Api.Contracts.Retrieval;
using HarrisCountyAI.Application.Search.Retrieval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HarrisCountyAI.Api.Controllers;

/// <summary>
/// Temporary debug endpoint for inspecting corpus retrieval results directly.
/// </summary>
/// <remarks>
/// Exists so retrieval quality can be exercised end to end before the Q&amp;A
/// flow lands on top of it; scheduled for removal once the Q&amp;A endpoints
/// make it redundant. Not part of the product API surface.
/// </remarks>
[ApiController]
[Route("api/debug/retrieval")]
[Authorize(Policy = AuthorizationPolicies.RequireAdministrator)]
public class RetrievalDebugController : ControllerBase
{
    private readonly IRetrievalService _retrievalService;

    public RetrievalDebugController(IRetrievalService retrievalService)
    {
        _retrievalService = retrievalService;
    }

    [HttpPost]
    [ProducesResponseType<IReadOnlyList<RetrievedChunk>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromBody] RetrievalDebugRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new ModelStateDictionary();

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            errors.AddModelError("query", "A query is required.");
        }

        var topK = request.TopK ?? RetrievalRequest.DefaultTopK;
        if (topK is < 1 or > RetrievalRequest.MaxTopK)
        {
            errors.AddModelError("topK", $"TopK must be between 1 and {RetrievalRequest.MaxTopK}.");
        }

        if (!errors.IsValid)
        {
            return ValidationProblem(errors);
        }

        var chunks = await _retrievalService.RetrieveAsync(
            new RetrievalRequest
            {
                Query = request.Query!,
                TopK = topK,
                Department = request.Department,
                PermitType = request.PermitType,
                DocumentType = request.DocumentType,
            },
            cancellationToken);

        return Ok(chunks);
    }
}
