using HarrisCountyAI.Application.Cases;

namespace HarrisCountyAI.Application.Documents.GetDocuments;

public sealed class GetDocumentsHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly IDocumentRepository _documentRepository;

    public GetDocumentsHandler(ICaseRepository caseRepository, IDocumentRepository documentRepository)
    {
        _caseRepository = caseRepository;
        _documentRepository = documentRepository;
    }

    /// <summary>Returns the case's documents, or null when the case does not exist.</summary>
    public async Task<IReadOnlyList<DocumentDto>?> HandleAsync(Guid caseId, CancellationToken cancellationToken = default)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, cancellationToken);
        if (@case is null)
        {
            return null;
        }

        var documents = await _documentRepository.GetByCaseIdAsync(caseId, cancellationToken);
        return documents.Select(DocumentDto.FromEntity).ToList();
    }
}
