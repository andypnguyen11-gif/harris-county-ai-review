using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.KnowledgeBase.UploadKnowledgeDocument;

public sealed class UploadKnowledgeDocumentHandler
{
    private readonly IKnowledgeDocumentRepository _repository;
    private readonly IDocumentStorageService _storage;

    public UploadKnowledgeDocumentHandler(
        IKnowledgeDocumentRepository repository,
        IDocumentStorageService storage)
    {
        _repository = repository;
        _storage = storage;
    }

    /// <summary>
    /// Stores the file in the knowledge-base blob container and persists the
    /// document metadata in the <see cref="Domain.Enums.KnowledgeDocumentIngestionStatus.Uploaded"/> state.
    /// </summary>
    public async Task<KnowledgeDocumentDto> HandleAsync(
        UploadKnowledgeDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        var documentId = Guid.NewGuid();
        var blobPath = DocumentBlobPathBuilder.ForKnowledgeDocument(documentId, command.FileName);

        await _storage.UploadAsync(
            DocumentStorageContainer.KnowledgeBase,
            blobPath,
            command.ContentType,
            command.Content,
            cancellationToken);

        var document = KnowledgeDocument.Create(
            documentId,
            command.Title,
            command.FileName,
            blobPath,
            command.Department,
            command.DocumentType,
            command.PermitType,
            command.Version,
            command.EffectiveDate,
            command.SourceUrl);

        await _repository.AddAsync(document, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return KnowledgeDocumentDto.FromEntity(document);
    }
}
