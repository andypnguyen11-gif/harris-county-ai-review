using HarrisCountyAI.Application.Cases;
using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.UnitTests.Application;

/// <summary>In-memory ICaseRepository test double.</summary>
internal sealed class FakeCaseRepository : ICaseRepository
{
    private readonly List<Case> _cases = [];

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyList<Case> Cases => _cases;

    public Task<Case?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cases.SingleOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Case>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Case>>(_cases.OrderByDescending(c => c.CreatedAt).ToList());

    public Task<string?> GetLatestCaseNumberAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cases
            .Where(c => c.CaseNumber.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c.CaseNumber, StringComparer.Ordinal)
            .Select(c => c.CaseNumber)
            .FirstOrDefault());

    public Task AddAsync(Case @case, CancellationToken cancellationToken = default)
    {
        _cases.Add(@case);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
