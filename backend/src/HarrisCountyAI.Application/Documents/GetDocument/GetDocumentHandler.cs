namespace HarrisCountyAI.Application.Documents.GetDocument;

public sealed class GetDocumentHandler
{
    private readonly IDocumentRepository _documentRepository;

    public GetDocumentHandler(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    /// <summary>
    /// Returns the document, or null when it does not exist or does not belong
    /// to the case.
    /// </summary>
    public async Task<DocumentDto?> HandleAsync(Guid caseId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(caseId, documentId, cancellationToken);
        return document is null ? null : DocumentDto.FromEntity(document);
    }
}
