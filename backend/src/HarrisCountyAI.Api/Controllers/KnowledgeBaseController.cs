using System.Globalization;
using HarrisCountyAI.Api.Contracts.KnowledgeBase;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.KnowledgeBase;
using HarrisCountyAI.Application.KnowledgeBase.DeactivateKnowledgeDocument;
using HarrisCountyAI.Application.KnowledgeBase.GetKnowledgeDocuments;
using HarrisCountyAI.Application.KnowledgeBase.UploadKnowledgeDocument;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HarrisCountyAI.Api.Controllers;

/// <summary>
/// Administration endpoints for the Harris County reference corpus.
/// </summary>
/// <remarks>
/// These endpoints are admin-only; authorization arrives with the security
/// milestone and is intentionally not enforced here yet.
/// </remarks>
[ApiController]
[Route("api/knowledge-base")]
public class KnowledgeBaseController : ControllerBase
{
    private readonly UploadKnowledgeDocumentHandler _uploadDocument;
    private readonly GetKnowledgeDocumentsHandler _getDocuments;
    private readonly DeactivateKnowledgeDocumentHandler _deactivateDocument;
    private readonly DocumentFileValidator _fileValidator;

    public KnowledgeBaseController(
        UploadKnowledgeDocumentHandler uploadDocument,
        GetKnowledgeDocumentsHandler getDocuments,
        DeactivateKnowledgeDocumentHandler deactivateDocument,
        DocumentFileValidator fileValidator)
    {
        _uploadDocument = uploadDocument;
        _getDocuments = getDocuments;
        _deactivateDocument = deactivateDocument;
        _fileValidator = fileValidator;
    }

    [HttpPost("documents")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<KnowledgeDocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadKnowledgeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // A fresh dictionary keeps error keys camelCased; form binding seeds
        // ModelState with PascalCase entries that lowercase keys would merge into.
        var errors = new ModelStateDictionary();

        RequireField(errors, request.Title, "title", "Title");
        RequireField(errors, request.Department, "department", "Department");
        RequireField(errors, request.DocumentType, "documentType", "Document type");
        RequireField(errors, request.PermitType, "permitType", "Permit type");

        var effectiveDate = ParseEffectiveDate(errors, request.EffectiveDate);

        if (request.File is null || request.File.Length == 0)
        {
            errors.AddModelError("file", "A file is required.");
        }
        else
        {
            var validation = _fileValidator.Validate(
                request.File.FileName, request.File.ContentType, request.File.Length);
            foreach (var error in validation.Errors)
            {
                errors.AddModelError("file", error);
            }
        }

        if (!errors.IsValid)
        {
            return ValidationProblem(errors);
        }

        await using var content = request.File!.OpenReadStream();
        var command = new UploadKnowledgeDocumentCommand(
            request.Title!,
            request.File.FileName,
            request.File.ContentType,
            content,
            request.Department!,
            request.DocumentType!,
            request.PermitType!,
            request.Version,
            effectiveDate,
            request.SourceUrl);

        var created = await _uploadDocument.HandleAsync(command, cancellationToken);

        return Created($"/api/knowledge-base/documents/{created.Id}", created);
    }

    [HttpGet("documents")]
    [ProducesResponseType<IReadOnlyList<KnowledgeDocumentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeDeactivated,
        CancellationToken cancellationToken)
    {
        var documents = await _getDocuments.HandleAsync(includeDeactivated, cancellationToken);
        return Ok(documents);
    }

    [HttpDelete("documents/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var deactivated = await _deactivateDocument.HandleAsync(id, cancellationToken);
        return deactivated ? NoContent() : NotFound();
    }

    private static void RequireField(ModelStateDictionary errors, string? value, string fieldName, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.AddModelError(fieldName, $"{label} is required.");
        }
    }

    /// <summary>Parses the optional effective date, recording a model error when invalid.</summary>
    private static DateOnly? ParseEffectiveDate(ModelStateDictionary errors, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        errors.AddModelError("effectiveDate", "Effective date must be an ISO 8601 date (yyyy-MM-dd).");
        return null;
    }
}
