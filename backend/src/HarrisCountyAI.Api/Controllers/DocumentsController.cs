using HarrisCountyAI.Api.Contracts.Documents;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.GetDocument;
using HarrisCountyAI.Application.Documents.GetDocumentContent;
using HarrisCountyAI.Application.Documents.GetDocuments;
using HarrisCountyAI.Application.Documents.ProcessDocument;
using HarrisCountyAI.Application.Documents.UploadDocument;
using HarrisCountyAI.Api.Authorization;
using HarrisCountyAI.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HarrisCountyAI.Api.Controllers;

[ApiController]
[Route("api/cases/{caseId:guid}/documents")]
[Authorize(Policy = AuthorizationPolicies.RequireReviewer)]
public class DocumentsController : ControllerBase
{
    /// <summary>
    /// Upper bound for the multipart request body: the validator's maximum
    /// file size plus headroom for the multipart framing and form fields.
    /// Files between the validator limit and this bound still get a clean 400
    /// from <see cref="DocumentFileValidator"/>.
    /// </summary>
    private const long MaxUploadRequestBytes = DocumentFileValidator.DefaultMaxFileSizeBytes + 1024 * 1024;

    private readonly UploadDocumentHandler _uploadDocument;
    private readonly GetDocumentsHandler _getDocuments;
    private readonly GetDocumentHandler _getDocument;
    private readonly GetDocumentContentHandler _getDocumentContent;
    private readonly ProcessDocumentHandler _processDocument;

    public DocumentsController(
        UploadDocumentHandler uploadDocument,
        GetDocumentsHandler getDocuments,
        GetDocumentHandler getDocument,
        GetDocumentContentHandler getDocumentContent,
        ProcessDocumentHandler processDocument)
    {
        _uploadDocument = uploadDocument;
        _getDocuments = getDocuments;
        _getDocument = getDocument;
        _getDocumentContent = getDocumentContent;
        _processDocument = processDocument;
    }

    [HttpPost]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(
        Guid caseId,
        [FromForm] UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // A fresh dictionary keeps the wire error keys camelCase; the form
        // binder pre-populates ModelState with property-cased keys ("File").
        var errors = new ModelStateDictionary();

        if (request.File is null || request.File.Length == 0)
        {
            errors.AddModelError("file", "A file is required.");
        }

        DocumentType? documentType = null;
        if (Enum.TryParse<DocumentType>(request.DocumentType, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            documentType = parsed;
        }
        else
        {
            errors.AddModelError(
                "documentType",
                $"Value must be one of: {string.Join(", ", Enum.GetNames<DocumentType>())}.");
        }

        if (!errors.IsValid)
        {
            return ValidationProblem(errors);
        }

        var file = request.File!;
        await using var content = file.OpenReadStream();
        var command = new UploadDocumentCommand(
            caseId,
            file.FileName,
            file.ContentType,
            file.Length,
            content,
            documentType!.Value);

        var result = await _uploadDocument.HandleAsync(command, cancellationToken);

        switch (result.Outcome)
        {
            case UploadDocumentOutcome.CaseNotFound:
                return NotFound();

            case UploadDocumentOutcome.InvalidFile:
                foreach (var error in result.Errors)
                {
                    errors.AddModelError("file", error);
                }

                return ValidationProblem(errors);

            default:
                var document = result.Document!;
                return CreatedAtAction(nameof(GetById), new { caseId, documentId = document.Id }, document);
        }
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll(Guid caseId, CancellationToken cancellationToken)
    {
        var documents = await _getDocuments.HandleAsync(caseId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    [HttpGet("{documentId:guid}")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid caseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _getDocument.HandleAsync(caseId, documentId, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    /// <summary>
    /// Runs the extraction pipeline (extract → normalize → persist → index)
    /// over an uploaded document, so its content becomes available to
    /// validation and to case-scoped retrieval. Also used to reprocess a failed
    /// document, or to re-run one after the applicant supplies a corrected file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately separate from upload: this step calls Azure Document
    /// Intelligence and the embedding and index path, so it is slow and fails
    /// independently. Folding it into the upload request would mean a
    /// Document Intelligence outage either discarded the stored file or turned
    /// a successful upload into an error.
    /// </para>
    /// <para>
    /// A run that fails returns <c>200 OK</c>, not a 5xx: the request itself
    /// was handled and its outcome durably recorded. The body reports the
    /// document's terminal <c>Failed</c> status and the reason, so a client can
    /// tell "the pipeline ran and failed, here is why" — which the reviewer
    /// resolves by fixing the file or retrying — apart from "the call never
    /// landed", which is a transport problem. A stuck document is therefore
    /// visible as <c>Failed</c> rather than sitting at <c>Uploaded</c> forever.
    /// </para>
    /// <para>
    /// The run is synchronous: the response arrives when the pipeline finishes.
    /// See the known limitations in <c>docs/testing/mvp-test-plan.md</c>.
    /// </para>
    /// </remarks>
    [HttpPost("{documentId:guid}/process")]
    [ProducesResponseType<ProcessDocumentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Process(Guid caseId, Guid documentId, CancellationToken cancellationToken)
    {
        var result = await _processDocument.HandleAsync(caseId, documentId, cancellationToken);

        return result is null
            ? Problem(
                title: "The document was not found.",
                detail: "No document with that id belongs to this case.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(result);
    }

    /// <summary>
    /// Streams the document's stored file so a reviewer can read it in place,
    /// inline rather than as a download. The whole file is served in one
    /// response — the blob stream is not seekable, so range requests are not
    /// supported, and the viewer navigates to a cited page client-side after
    /// loading. Scoped to the case in the route, so a document id alone never
    /// reaches another case's file. A document whose
    /// stored file has gone missing returns 404 with a distinct explanation,
    /// which the viewer surfaces as "the file is unavailable" rather than
    /// "no such document".
    /// </summary>
    [HttpGet("{documentId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContent(Guid caseId, Guid documentId, CancellationToken cancellationToken)
    {
        var result = await _getDocumentContent.HandleAsync(caseId, documentId, cancellationToken);

        switch (result.Outcome)
        {
            case DocumentContentOutcome.DocumentNotFound:
                return Problem(
                    title: "The document was not found.",
                    detail: "No document with that id belongs to this case.",
                    statusCode: StatusCodes.Status404NotFound);

            case DocumentContentOutcome.FileUnavailable:
                return Problem(
                    title: "The document file is unavailable.",
                    detail: "The document exists, but its stored file could not be read.",
                    statusCode: StatusCodes.Status404NotFound);

            default:
                // Inline: the viewer renders the file rather than downloading it.
                Response.Headers.ContentDisposition = $"inline; filename=\"{result.FileName}\"";
                return File(result.Content!, result.ContentType!);
        }
    }
}
