using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Documents.GetDocumentContent;

/// <summary>
/// Opens a case document's stored file so a reviewer can read the page a
/// citation points at.
/// </summary>
/// <remarks>
/// Case-scoped by contract: the document is looked up by case id and document
/// id together, so a document id alone never reaches another case's file. A
/// record whose blob has gone missing is reported as
/// <see cref="DocumentContentOutcome.FileUnavailable"/> — distinct from "no
/// such document" — so the viewer can say which of the two happened.
/// </remarks>
public sealed class GetDocumentContentHandler
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorageService _storageService;
    private readonly ILogger<GetDocumentContentHandler> _logger;

    public GetDocumentContentHandler(
        IDocumentRepository documentRepository,
        IDocumentStorageService storageService,
        ILogger<GetDocumentContentHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(documentRepository);
        ArgumentNullException.ThrowIfNull(storageService);

        _documentRepository = documentRepository;
        _storageService = storageService;
        _logger = logger ?? NullLogger<GetDocumentContentHandler>.Instance;
    }

    public async Task<DocumentContentResult> HandleAsync(
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(caseId, documentId, cancellationToken);
        if (document is null)
        {
            return DocumentContentResult.NotFound(DocumentContentOutcome.DocumentNotFound);
        }

        try
        {
            var content = await _storageService.DownloadAsync(
                DocumentStorageContainer.CaseDocuments,
                document.BlobPath,
                cancellationToken);

            return new DocumentContentResult
            {
                Outcome = DocumentContentOutcome.Found,
                Content = content,
                FileName = document.FileName,
                ContentType = DocumentContentTypes.FromFileName(document.FileName),
            };
        }
        catch (FileNotFoundException exception)
        {
            _logger.LogWarning(
                exception,
                "Document {DocumentId} on case {CaseId} has no stored file at {BlobPath}.",
                documentId,
                caseId,
                document.BlobPath);
            return DocumentContentResult.NotFound(DocumentContentOutcome.FileUnavailable);
        }
    }
}
