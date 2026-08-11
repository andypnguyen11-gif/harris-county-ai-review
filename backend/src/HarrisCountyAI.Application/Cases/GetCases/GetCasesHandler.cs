namespace HarrisCountyAI.Application.Cases.GetCases;

public sealed class GetCasesHandler
{
    private readonly ICaseRepository _repository;

    public GetCasesHandler(ICaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CaseDto>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var cases = await _repository.GetAllAsync(cancellationToken);
        return cases.Select(CaseDto.FromEntity).ToList();
    }
}
