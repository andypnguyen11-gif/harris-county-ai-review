using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.UnitTests.Application;

/// <summary>In-memory IDocumentRepository test double.</summary>
internal sealed class FakeDocumentRepository : IDocumentRepository
{
    private readonly List<Document> _documents = [];

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyList<Document> Documents => _documents;

    public Task<Document?> GetByIdAsync(Guid caseId, Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.SingleOrDefault(d => d.Id == documentId && d.CaseId == caseId));

    public Task<IReadOnlyList<Document>> GetByCaseIdAsync(Guid caseId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>(_documents
            .Where(d => d.CaseId == caseId)
            .OrderBy(d => d.CreatedAt)
            .ToList());

    public Task AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        _documents.Add(document);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
