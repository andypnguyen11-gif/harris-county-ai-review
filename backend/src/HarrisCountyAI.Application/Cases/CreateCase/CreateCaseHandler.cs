using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.Cases.CreateCase;

public sealed class CreateCaseHandler
{
    private const string CaseNumberPrefixFormat = "HC-{0}-";

    private readonly ICaseRepository _repository;

    public CreateCaseHandler(ICaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<CaseDto> HandleAsync(CreateCaseCommand command, CancellationToken cancellationToken = default)
    {
        var caseNumber = await GenerateCaseNumberAsync(cancellationToken);
        var @case = Case.Create(caseNumber, command.Name, command.WorkflowType);

        await _repository.AddAsync(@case, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return CaseDto.FromEntity(@case);
    }

    private async Task<string> GenerateCaseNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = string.Format(CaseNumberPrefixFormat, DateTime.UtcNow.Year);
        var latest = await _repository.GetLatestCaseNumberAsync(prefix, cancellationToken);

        var next = 1;
        if (latest is not null && int.TryParse(latest.AsSpan(prefix.Length), out var latestSequence))
        {
            next = latestSequence + 1;
        }

        return $"{prefix}{next:D4}";
    }
}
