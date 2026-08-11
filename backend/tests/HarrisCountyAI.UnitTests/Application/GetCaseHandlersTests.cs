using HarrisCountyAI.Application.Cases.GetCase;
using HarrisCountyAI.Application.Cases.GetCases;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Application;

public class GetCaseHandlersTests
{
    private readonly FakeCaseRepository _repository = new();

    [Fact]
    public async Task GetCase_Returns_Dto_For_Existing_Case()
    {
        var @case = Case.Create("HC-2026-0001", "Existing", WorkflowType.FloodplainDevelopmentPermit);
        await _repository.AddAsync(@case);

        var dto = await new GetCaseHandler(_repository).HandleAsync(@case.Id);

        Assert.NotNull(dto);
        Assert.Equal(@case.Id, dto.Id);
        Assert.Equal("HC-2026-0001", dto.CaseNumber);
        Assert.Equal("Existing", dto.Name);
    }

    [Fact]
    public async Task GetCase_Returns_Null_For_Unknown_Id()
    {
        var dto = await new GetCaseHandler(_repository).HandleAsync(Guid.NewGuid());

        Assert.Null(dto);
    }

    [Fact]
    public async Task GetCases_Returns_All_Cases()
    {
        await _repository.AddAsync(Case.Create("HC-2026-0001", "One", WorkflowType.FloodplainDevelopmentPermit));
        await _repository.AddAsync(Case.Create("HC-2026-0002", "Two", WorkflowType.FloodplainDevelopmentPermit));

        var dtos = await new GetCasesHandler(_repository).HandleAsync();

        Assert.Equal(2, dtos.Count);
        Assert.Contains(dtos, d => d.Name == "One");
        Assert.Contains(dtos, d => d.Name == "Two");
    }

    [Fact]
    public async Task GetCases_Returns_Empty_List_When_No_Cases()
    {
        var dtos = await new GetCasesHandler(_repository).HandleAsync();

        Assert.Empty(dtos);
    }
}
