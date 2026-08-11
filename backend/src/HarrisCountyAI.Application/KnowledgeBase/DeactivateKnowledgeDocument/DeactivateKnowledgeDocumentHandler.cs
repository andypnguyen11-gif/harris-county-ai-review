namespace HarrisCountyAI.Application.KnowledgeBase.DeactivateKnowledgeDocument;

public sealed class DeactivateKnowledgeDocumentHandler
{
    private readonly IKnowledgeDocumentRepository _repository;

    public DeactivateKnowledgeDocumentHandler(IKnowledgeDocumentRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Soft-deletes the document. Returns <c>false</c> when no document with
    /// <paramref name="id"/> exists. Deactivating an already-deactivated
    /// document succeeds without changes.
    /// </summary>
    public async Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return false;
        }

        document.Deactivate();
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
