using HarrisCountyAI.Application.Cases;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Documents.UploadDocument;

/// <summary>
/// Uploads a file to a case: verifies the case exists, validates the file,
/// stores the content in the case-documents container, and persists a
/// <see cref="Document"/> record with status
/// <see cref="DocumentProcessingStatus.Uploaded"/>.
/// </summary>
public sealed class UploadDocumentHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorageService _storageService;
    private readonly DocumentFileValidator _fileValidator;

    public UploadDocumentHandler(
        ICaseRepository caseRepository,
        IDocumentRepository documentRepository,
        IDocumentStorageService storageService,
        DocumentFileValidator fileValidator)
    {
        _caseRepository = caseRepository;
        _documentRepository = documentRepository;
        _storageService = storageService;
        _fileValidator = fileValidator;
    }

    public async Task<UploadDocumentResult> HandleAsync(UploadDocumentCommand command, CancellationToken cancellationToken = default)
    {
        var @case = await _caseRepository.GetByIdAsync(command.CaseId, cancellationToken);
        if (@case is null)
        {
            return UploadDocumentResult.CaseNotFound();
        }

        var validation = _fileValidator.Validate(command.FileName, command.ContentType, command.FileSizeBytes);
        if (!validation.IsValid)
        {
            return UploadDocumentResult.InvalidFile(validation.Errors);
        }

        // The document id is generated up front so the blob path can embed it
        // before the entity is created.
        var documentId = Guid.NewGuid();
        var blobPath = DocumentBlobPathBuilder.ForCaseDocument(command.CaseId, documentId, command.FileName);

        await _storageService.UploadAsync(
            DocumentStorageContainer.CaseDocuments,
            blobPath,
            command.ContentType,
            command.Content,
            cancellationToken);

        var document = Document.Create(documentId, command.CaseId, command.FileName, blobPath, command.DocumentType);
        document.SetProcessingStatus(DocumentProcessingStatus.Uploaded);

        await _documentRepository.AddAsync(document, cancellationToken);
        await _documentRepository.SaveChangesAsync(cancellationToken);

        return UploadDocumentResult.Uploaded(DocumentDto.FromEntity(document));
    }
}
