namespace HarrisCountyAI.Application.Cases.UpdateCase;

public sealed class UpdateCaseHandler
{
    private readonly ICaseRepository _repository;

    public UpdateCaseHandler(ICaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<CaseDto?> HandleAsync(Guid id, UpdateCaseCommand command, CancellationToken cancellationToken = default)
    {
        var @case = await _repository.GetByIdAsync(id, cancellationToken);
        if (@case is null)
        {
            return null;
        }

        if (command.Name is not null)
        {
            @case.Rename(command.Name);
        }

        if (command.Status is not null)
        {
            @case.SetStatus(command.Status.Value);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return CaseDto.FromEntity(@case);
    }
}
