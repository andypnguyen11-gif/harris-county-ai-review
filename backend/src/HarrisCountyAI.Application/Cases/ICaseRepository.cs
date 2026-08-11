using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.Cases;

/// <summary>Persistence abstraction for cases so the application layer stays free of EF Core.</summary>
public interface ICaseRepository
{
    Task<Case?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Case>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the highest existing case number starting with <paramref name="prefix"/>, or null.</summary>
    Task<string?> GetLatestCaseNumberAsync(string prefix, CancellationToken cancellationToken = default);

    Task AddAsync(Case @case, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
