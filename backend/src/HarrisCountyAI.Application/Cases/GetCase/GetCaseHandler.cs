namespace HarrisCountyAI.Application.Cases.GetCase;

public sealed class GetCaseHandler
{
    private readonly ICaseRepository _repository;

    public GetCaseHandler(ICaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<CaseDto?> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var @case = await _repository.GetByIdAsync(id, cancellationToken);
        return @case is null ? null : CaseDto.FromEntity(@case);
    }
}
