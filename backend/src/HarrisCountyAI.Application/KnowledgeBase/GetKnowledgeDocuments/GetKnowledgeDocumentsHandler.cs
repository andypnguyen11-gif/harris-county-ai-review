namespace HarrisCountyAI.Application.KnowledgeBase.GetKnowledgeDocuments;

public sealed class GetKnowledgeDocumentsHandler
{
    private readonly IKnowledgeDocumentRepository _repository;

    public GetKnowledgeDocumentsHandler(IKnowledgeDocumentRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<KnowledgeDocumentDto>> HandleAsync(
        bool includeDeactivated = false,
        CancellationToken cancellationToken = default)
    {
        var documents = await _repository.GetAllAsync(includeDeactivated, cancellationToken);
        return documents.Select(KnowledgeDocumentDto.FromEntity).ToList();
    }
}
